using System.Text.Json;

namespace SmartPaste.Core;

// Which service to ask Jev through. Auto uses the TypeSafe key first, then the OpenRouter key.
public enum JevRoutePreference { Auto, TypeSafe, OpenRouter }

// A service that answers Jev questions.
public enum JevEndpoint { TypeSafe, OpenRouter }

public sealed record JevCredentials(
    string? TypeSafeKey, string? OpenRouterKey, JevRoutePreference Preference = JevRoutePreference.Auto)
{
    // The routes to try, in order, limited to those with a key.
    public IReadOnlyList<JevEndpoint> Routes
    {
        get
        {
            JevEndpoint[] order = Preference switch
            {
                JevRoutePreference.TypeSafe => [JevEndpoint.TypeSafe],
                JevRoutePreference.OpenRouter => [JevEndpoint.OpenRouter],
                _ => [JevEndpoint.TypeSafe, JevEndpoint.OpenRouter]
            };
            return order.Where(route => !string.IsNullOrWhiteSpace(KeyFor(route))).ToArray();
        }
    }

    public bool HasKey => Routes.Count > 0;

    public string? KeyFor(JevEndpoint route) => route == JevEndpoint.TypeSafe ? TypeSafeKey : OpenRouterKey;

    // Keys never appear in logs or debugger displays of this record.
    public override string ToString() =>
        $"JevCredentials {{ TypeSafeKey = {Mask(TypeSafeKey)}, OpenRouterKey = {Mask(OpenRouterKey)}, Preference = {Preference} }}";

    private static string Mask(string? key) => string.IsNullOrWhiteSpace(key) ? "none" : "set";
}

// Jev's answer to one question.
public sealed record JevChoice(
    string Choice, double Confidence, IReadOnlyDictionary<string, double> Probabilities, JevEndpoint Route)
{
    private static readonly IReadOnlyDictionary<JevEndpoint, ServiceFailure> NoFailures =
        new Dictionary<JevEndpoint, ServiceFailure>();

    // Routes tried before the one that answered, and why each failed (for example a rejected TypeSafe key).
    public IReadOnlyDictionary<JevEndpoint, ServiceFailure> FailedRoutes { get; init; } = NoFailures;

    public bool IsNone => Choice == JevQuestion.NoneOption;
}

public interface IJevChooser
{
    // Asks Jev one question, all routes sharing one deadline. Every failure is a ServiceException (NoKey when no
    // key is usable); cancellation of ct surfaces as OperationCanceledException.
    Task<JevChoice> ChooseAsync(JevQuestion question, TimeSpan deadline, CancellationToken ct);
}

public sealed class JevClient : IJevChooser
{
    public const string TypeSafeUrl = "https://api.typesafe.ai/v1/systemone";
    public const string OpenRouterUrl = OpenRouterApi.DecisionsUrl;
    public const string TypeSafeModel = "jev-latest";
    public const string OpenRouterModel = "typesafe/jev-1.13";

    // Keys the answer in the request and response; Jev itself never sees it.
    internal const string QuestionId = "field_value";

    private readonly HttpClient _http;

    public JevClient(HttpClient http, JevCredentials credentials)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public JevCredentials Credentials { get; }

    public async Task<JevChoice> ChooseAsync(JevQuestion question, TimeSpan deadline, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(question);
        var routes = Credentials.Routes;
        if (routes.Count == 0) throw new ServiceException(ServiceFailure.NoKey);

        using var timer = ServiceHttp.Deadline(deadline, ct);
        var failures = new Dictionary<JevEndpoint, ServiceException>();
        foreach (var route in routes)
        {
            if (timer.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                failures[route] = new ServiceException(ServiceFailure.Timeout);
                continue;
            }
            try
            {
                var json = await SendAsync(route, question, timer.Token, ct).ConfigureAwait(false);
                var choice = ParseResponse(json, route, question.OptionNames);
                return choice with { FailedRoutes = failures.ToDictionary(p => p.Key, p => p.Value.Failure) };
            }
            catch (ServiceException ex)
            {
                // A rejected key, no credits, or an outage on one route: the next route may still answer.
                failures[route] = ex;
            }
        }

        var reported = failures.Values.MaxBy(ex => ServiceException.Actionability(ex.Failure))!;
        throw new ServiceException(reported.Failure, reported.StatusCode, reported);
    }

    // Reads Jev's answer. Tolerates a missing choice (the most probable option wins) and a missing confidence
    // (computed from the probabilities). Any other problem is a ServiceException with BadResponse.
    public static JevChoice ParseResponse(string json, JevEndpoint route) => Parse(json, route, null);

    // As above, and the choice must be one of the options that were sent.
    public static JevChoice ParseResponse(string json, JevEndpoint route, IReadOnlyCollection<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Parse(json, route, options);
    }

    // TypeSafe's documented confidence for a choice: the top probability rescaled so that an even spread over n
    // options gives 0 and certainty gives 1. It is lower than the raw top probability.
    public static double ChoiceConfidence(double topProbability, int optionCount)
    {
        var top = Math.Clamp(topProbability, 0, 1);
        if (optionCount <= 1) return top;
        var even = 1.0 / optionCount;
        return Math.Clamp((top - even) / (1 - even), 0, 1);
    }

    private async Task<string> SendAsync(JevEndpoint route, JevQuestion question, CancellationToken token, CancellationToken ct)
    {
        var (url, model) = route == JevEndpoint.TypeSafe ? (TypeSafeUrl, TypeSafeModel) : (OpenRouterUrl, OpenRouterModel);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = ServiceHttp.Json(question.ToRequestBody(model, QuestionId))
        };
        ServiceHttp.Authorize(request, Credentials.KeyFor(route));
        if (route == JevEndpoint.OpenRouter) OpenRouterApi.Attribute(request);
        return await ServiceHttp.SendAsync(_http, request, token, ct).ConfigureAwait(false);
    }

    private static JevChoice Parse(string json, JevEndpoint route, IReadOnlyCollection<string>? options)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // The parser's message can quote the body, so it is not kept.
            throw new ServiceException(ServiceFailure.BadResponse);
        }

        using (document)
        {
            var answer = FindAnswer(document.RootElement);
            var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
            if (answer.TryGetProperty("probabilities", out var node) && node.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in node.EnumerateObject())
                {
                    if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetDouble(out var p) &&
                        double.IsFinite(p) && (options is null || options.Contains(item.Name)))
                        probabilities[item.Name] = Math.Clamp(p, 0, 1);
                }
            }

            var choice = answer.TryGetProperty("choice", out var choiceNode) && choiceNode.ValueKind == JsonValueKind.String
                ? choiceNode.GetString()
                : null;
            if (string.IsNullOrEmpty(choice) && probabilities.Count > 0)
                choice = probabilities.MaxBy(p => p.Value).Key;
            if (string.IsNullOrEmpty(choice)) throw new ServiceException(ServiceFailure.BadResponse);

            var known = options ?? (probabilities.Count > 0 ? probabilities.Keys : null);
            if (known is not null && !known.Contains(choice)) throw new ServiceException(ServiceFailure.BadResponse);

            double confidence;
            if (answer.TryGetProperty("confidence", out var confidenceNode) &&
                confidenceNode.ValueKind == JsonValueKind.Number &&
                confidenceNode.TryGetDouble(out var parsed) && double.IsFinite(parsed))
                confidence = Math.Clamp(parsed, 0, 1);
            else if (probabilities.TryGetValue(choice, out var top))
                confidence = ChoiceConfidence(top, options?.Count ?? probabilities.Count);
            else
                throw new ServiceException(ServiceFailure.BadResponse);

            return new JevChoice(choice, confidence, probabilities, route);
        }
    }

    // The answer sits under our question id. A response with exactly one answer under another id is accepted too.
    private static JsonElement FindAnswer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
            throw new ServiceException(ServiceFailure.BadResponse);
        if (answers.TryGetProperty(QuestionId, out var answer) && answer.ValueKind == JsonValueKind.Object) return answer;
        var all = answers.EnumerateObject().ToArray();
        if (all.Length == 1 && all[0].Value.ValueKind == JsonValueKind.Object) return all[0].Value;
        throw new ServiceException(ServiceFailure.BadResponse);
    }
}

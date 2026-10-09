using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartPaste.Core;

public sealed record JevChoice(
    string Choice,
    double Confidence,
    IReadOnlyDictionary<string, double> Probabilities,
    string Route);

public sealed class JevException(string message) : Exception(message);

public interface IJevChooser
{
    Task<JevChoice> ChooseAsync(
        string state,
        string instructions,
        IReadOnlyDictionary<string, string> criteria,
        CancellationToken ct);
}

public sealed class JevClient : IJevChooser
{
    private readonly HttpClient _http;
    private readonly string? _typesafeKey;
    private readonly string? _openrouterKey;
    private readonly TimeSpan _totalTimeout;

    public JevClient(HttpClient http, string? typesafeKey, string? openrouterKey, TimeSpan? totalTimeout = null)
    {
        _http = http;
        _typesafeKey = typesafeKey;
        _openrouterKey = openrouterKey;
        _totalTimeout = totalTimeout ?? TimeSpan.FromMilliseconds(1500);
    }

    public async Task<JevChoice> ChooseAsync(
        string state,
        string instructions,
        IReadOnlyDictionary<string, string> criteria,
        CancellationToken ct)
    {
        var failures = new List<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_totalTimeout);

        foreach (var route in new[]
        {
            new Route("typesafe", "https://api.typesafe.ai/v1/systemone", "jev-latest", _typesafeKey),
            new Route("openrouter", "https://openrouter.ai/api/alpha/decisions", "typesafe/jev-1.13", _openrouterKey)
        })
        {
            if (string.IsNullOrWhiteSpace(route.Key))
            {
                failures.Add($"{route.Name}: no key");
                continue;
            }

            if (deadline.IsCancellationRequested)
            {
                failures.Add($"{route.Name}: shared deadline expired");
                continue;
            }

            try
            {
                var body = new
                {
                    model = route.Model,
                    state,
                    questions = new
                    {
                        pick = new { type = "choice", instructions, criteria }
                    }
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, route.Url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.Key);
                using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{route.Name}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
                return ParseResponse(json, route.Name);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add($"{route.Name}: {ex.GetType().Name}");
            }
        }

        throw new JevException(string.Join("; ", failures));
    }

    public static JevChoice ParseResponse(string json, string route)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("answers", out var answers) ||
            !answers.TryGetProperty("pick", out var pick) ||
            pick.ValueKind != JsonValueKind.Object ||
            !pick.TryGetProperty("probabilities", out var probabilitiesNode) ||
            probabilitiesNode.ValueKind != JsonValueKind.Object)
            throw new FormatException("Jev response is missing the pick probabilities.");

        var probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in probabilitiesNode.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetDouble(out var probability))
                probabilities[item.Name] = probability;
        }

        string? choice = null;
        if (pick.TryGetProperty("choice", out var choiceNode) && choiceNode.ValueKind == JsonValueKind.String)
            choice = choiceNode.GetString();
        if (string.IsNullOrWhiteSpace(choice) && probabilities.Count > 0)
            choice = probabilities.MaxBy(item => item.Value).Key;
        if (string.IsNullOrWhiteSpace(choice))
            throw new FormatException("Jev response has no choice.");

        var confidence = pick.TryGetProperty("confidence", out var confidenceNode) &&
                         confidenceNode.ValueKind == JsonValueKind.Number &&
                         confidenceNode.TryGetDouble(out var parsedConfidence)
            ? parsedConfidence
            : probabilities.GetValueOrDefault(choice);

        return new JevChoice(choice, confidence, probabilities, route);
    }

    private sealed record Route(string Name, string Url, string Model, string? Key);
}

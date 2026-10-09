using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartPaste.Core;

public sealed class OpenRouterParser
{
    public const string Model = "anthropic/claude-haiku-4.5";
    public static string SystemPrompt { get; } =
        "Extract every distinct entity from the source, including people, billing addresses, shipping addresses, and companies. " +
        "Reply with JSON only in this shape: {\"entities\":[{\"label\":\"person\",\"fields\":{\"first_name\":\"...\"}}]}. " +
        "Use these standard field keys: first_name, middle_name, last_name, full_name, street, unit, city, state, zip, country, email, phone, company, title, date_of_birth, website, linkedin. " +
        "Use extra snake_case keys only for other clearly labeled facts. Keep values verbatim from the source. " +
        "Always fill full_name when a name is present. Keep state as written. Omit absent keys. Never invent values.";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;

    public OpenRouterParser(HttpClient http, string apiKey, TimeSpan? timeout = null)
    {
        _http = http;
        _apiKey = apiKey;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public Task<Profile> ParseTextAsync(string text, DateTimeOffset now, CancellationToken ct) =>
        ParseAsync(text, now, ct);

    public Task<Profile> ParseImageAsync(byte[] png, DateTimeOffset now, CancellationToken ct)
    {
        var content = new object[]
        {
            new { type = "text", text = "Extract the entities in this screenshot." },
            new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(png) } }
        };
        return ParseAsync(content, now, ct);
    }

    private async Task<Profile> ParseAsync(object userContent, DateTimeOffset now, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout);
        var body = new
        {
            model = Model,
            temperature = 0,
            max_tokens = 2000,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userContent }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            throw new FormatException("OpenRouter response has no message content.");

        return ProfileParser.Parse(content.GetString()!, now);
    }
}

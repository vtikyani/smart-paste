using System.Text.Json;

namespace SmartPaste.Core;

// The outcome of a key check, with a short message for the settings page.
public sealed record KeyCheckResult(bool Ok, ServiceFailure? Failure, string Message);

// Cheap checks that a saved key works, for the settings page and the welcome tour. They send nothing the user copied.
public static class ConnectionChecks
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public const string TypeSafeModelsUrl = "https://api.typesafe.ai/v1/models";

    // GET /api/v1/key describes the key: it fails with 401 for a bad key and reports what the key may still spend.
    public static async Task<KeyCheckResult> CheckOpenRouterKeyAsync(HttpClient http, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            var json = await GetAsync(http, OpenRouterApi.KeyInfoUrl, key, ct).ConfigureAwait(false);
            return ServiceHttp.ReadJson(json, ReadOpenRouterKey);
        }
        catch (ServiceException ex)
        {
            return Failed(ex, "OpenRouter");
        }
    }

    // GET /v1/models lists the models the key's account may use; it fails with 401 for a bad key and costs nothing.
    // Only the status matters, unless the body is an error envelope sent with HTTP 200.
    public static async Task<KeyCheckResult> CheckTypeSafeKeyAsync(HttpClient http, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            var body = await GetAsync(http, TypeSafeModelsUrl, key, ct).ConfigureAwait(false);
            if (ServiceHttp.ErrorIn(body) is { } error) throw error;
        }
        catch (ServiceException ex)
        {
            return Failed(ex, "TypeSafe");
        }
        return new KeyCheckResult(true, null, "Key works.");
    }

    private static async Task<string> GetAsync(HttpClient http, string url, string key, CancellationToken ct)
    {
        using var timer = ServiceHttp.Deadline(RequestTimeout, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ServiceHttp.Authorize(request, key);
        return await ServiceHttp.SendAsync(http, request, timer.Token, ct).ConfigureAwait(false);
    }

    // Runs inside ServiceHttp.ReadJson: any shape this does not expect becomes BadResponse there.
    private static KeyCheckResult ReadOpenRouterKey(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new ServiceException(ServiceFailure.BadResponse);
        // Management keys can manage the account but cannot run models.
        if (IsTrue(data, "is_management_key") || IsTrue(data, "is_provisioning_key"))
            return new KeyCheckResult(false, ServiceFailure.Unauthorized,
                "This is an OpenRouter management key. Create a regular API key instead.");
        var spent = Number(data, "limit_remaining") is <= 0 ||
                    (data.TryGetProperty("effective_budget", out var budget) && budget.ValueKind == JsonValueKind.Object &&
                     Number(budget, "remaining_usd") is <= 0);
        return spent
            ? new KeyCheckResult(false, ServiceFailure.PaymentRequired, "This key has no credits left.")
            : new KeyCheckResult(true, null, "Key works.");
    }

    private static KeyCheckResult Failed(ServiceException error, string service) => new(false, error.Failure, error.Failure switch
    {
        ServiceFailure.NoKey => "Enter a key first.",
        // 403: the service knows the key but does not let it do this (a restricted key), so "rejected" would mislead.
        ServiceFailure.Unauthorized when error.StatusCode == 403 => "This key isn't allowed to make this request.",
        ServiceFailure.Unauthorized => $"{service} rejected this key.",
        ServiceFailure.PaymentRequired => "This key has no credits left.",
        ServiceFailure.RateLimited => $"{service} is busy with too many requests. Try again in a minute.",
        ServiceFailure.Timeout => $"{service} did not answer in time. Try again.",
        ServiceFailure.Network => $"Can't reach {service}. Check your internet connection.",
        ServiceFailure.ServerError => $"{service} is having trouble right now. Try again later.",
        _ => $"{service} sent an answer Smart Paste could not read. Try again later."
    });

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;
}

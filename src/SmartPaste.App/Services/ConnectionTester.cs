using System.Net.Http;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// The Test buttons in Settings and the welcome tour. Core's checks already speak plain English and never throw for
// expected failures (a bad key, no credits, no connection).
internal sealed class ConnectionTester(HttpClient http) : IConnectionTester
{
    public async Task<ConnectionCheck> CheckOpenRouterAsync(string apiKey, CancellationToken ct)
    {
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(http, apiKey, ct);
        return new ConnectionCheck(result.Ok, result.Message);
    }

    public async Task<ConnectionCheck> CheckTypeSafeAsync(string apiKey, CancellationToken ct)
    {
        var result = await ConnectionChecks.CheckTypeSafeKeyAsync(http, apiKey, ct);
        return new ConnectionCheck(result.Ok, result.Message);
    }
}

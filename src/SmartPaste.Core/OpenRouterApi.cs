namespace SmartPaste.Core;

// Shared details of OpenRouter requests (parsing, Jev decisions, key checks).
internal static class OpenRouterApi
{
    public const string ChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
    public const string DecisionsUrl = "https://openrouter.ai/api/alpha/decisions";
    public const string KeyInfoUrl = "https://openrouter.ai/api/v1/key";

    // App attribution (https://openrouter.ai/docs/app-attribution): usage is credited to Smart Paste.
    // Both values are fixed; nothing about the user goes into these headers.
    public const string AppUrl = "https://github.com/vtikyani/smart-paste";
    public const string AppTitle = "Smart Paste";

    public static void Attribute(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("HTTP-Referer", AppUrl);
        request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", AppTitle);
    }
}

using System.Net;
using System.Text.Json;
using SmartPaste.Core;

public class OpenRouterParserTests
{
    [Fact] public async Task TextRequestUsesExpectedShapeAndParsesReply()
    {
        string? body = null;
        using var http = new HttpClient(new Handler(async request => {
            body = await request.Content!.ReadAsStringAsync();
            Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.ToString());
            Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());
            return Reply();
        }));
        var profile = await new OpenRouterParser(http, "test-key").ParseTextAsync("Ada Lovelace", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal("Ada", profile.ValuesFor(FieldKeys.FirstName).Single().Value);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(OpenRouterParser.Model, doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("Ada Lovelace", doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
    }
    [Fact] public async Task ImageRequestHasPngDataUrl()
    {
        string? body = null;
        using var http = new HttpClient(new Handler(async request => { body = await request.Content!.ReadAsStringAsync(); return Reply(); }));
        await new OpenRouterParser(http, "test-key").ParseImageAsync([1, 2, 3], DateTimeOffset.UtcNow, CancellationToken.None);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("data:image/png;base64,AQID", doc.RootElement.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString());
    }
    private static HttpResponseMessage Reply() => new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"entities\\\":[{\\\"label\\\":\\\"person\\\",\\\"fields\\\":{\\\"first_name\\\":\\\"Ada\\\"}}]}\"}}]}") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

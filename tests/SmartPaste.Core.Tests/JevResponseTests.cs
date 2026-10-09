using System.Net;
using System.Text;
using SmartPaste.Core;

public class JevResponseTests
{
    private const string TypeSafe = "{\"model\":\"jev-1.13.0\",\"answers\":{\"pick\":{\"type\":\"choice\",\"choice\":\"zip\",\"confidence\":1.0,\"probabilities\":{\"city\":0.0,\"none\":0.0,\"zip\":1.0,\"state\":0.0}}},\"usage\":{\"input_tokens\":374,\"output_tokens\":45}}";
    private const string OpenRouter = "{\"model\":\"typesafe/jev-1.13-20260917\",\"answers\":{\"pick\":{\"type\":\"choice\",\"choice\":\"zip\",\"probabilities\":{\"none\":0,\"city\":0,\"zip\":1,\"state\":0},\"confidence\":1}},\"usage\":{\"input_tokens\":374,\"output_tokens\":45,\"cost\":0.000015708},\"id\":\"gen-dec-1\",\"provider\":\"TypeSafe\"}";
    [Fact] public void CapturedResponses() { Assert.Equal("zip", JevClient.ParseResponse(TypeSafe, "typesafe").Choice); Assert.Equal("zip", JevClient.ParseResponse(OpenRouter, "openrouter").Choice); }
    [Fact] public void MissingChoiceAndConfidenceUseProbability()
    {
        const string json = "{\"answers\":{\"pick\":{\"probabilities\":{\"city\":0.2,\"zip\":0.8}}}}";
        var result = JevClient.ParseResponse(json, "test");
        Assert.Equal("zip", result.Choice);
        Assert.Equal(0.8, result.Confidence);
    }
    [Fact] public async Task FallsBackAndPostsCorrectBody()
    {
        var calls = new List<(string Host, string Body, string Auth)>();
        using var http = new HttpClient(new Handler(async request => {
            var body = await request.Content!.ReadAsStringAsync();
            calls.Add((request.RequestUri!.Host, body, request.Headers.Authorization!.ToString()));
            return new HttpResponseMessage(request.RequestUri!.Host == "api.typesafe.ai" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK) { Content = new StringContent(OpenRouter) };
        }));
        var choice = await new JevClient(http, "ts-key", "or-key").ChooseAsync("field", "instructions", new Dictionary<string,string>{{"zip","Postal"}}, CancellationToken.None);
        Assert.Equal("openrouter", choice.Route);
        Assert.Equal(2, calls.Count);
        Assert.Contains("\"questions\":{\"pick\":{\"type\":\"choice\"", calls[0].Body);
        Assert.Contains("\"model\":\"jev-latest\"", calls[0].Body);
        Assert.Contains("\"model\":\"typesafe/jev-1.13\"", calls[1].Body);
        Assert.Equal("Bearer ts-key", calls[0].Auth);
    }
    [Fact] public async Task BothFailThrow()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var error = await Assert.ThrowsAsync<JevException>(() => new JevClient(http, "a", "b").ChooseAsync("x", "y", new Dictionary<string,string>(), CancellationToken.None));
        Assert.Contains("typesafe", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openrouter", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task UnexpectedFirstRouteExceptionFallsBack()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("handler failure") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OpenRouter) });
        }));
        var result = await new JevClient(http, "a", "b").ChooseAsync("x", "y", new Dictionary<string,string>(), CancellationToken.None);
        Assert.Equal("openrouter", result.Route);
        Assert.Equal(2, calls);
    }
    [Fact] public async Task NoKeysThrow()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("should not call")));
        await Assert.ThrowsAsync<JevException>(() => new JevClient(http, null, null).ChooseAsync("x", "y", new Dictionary<string,string>(), CancellationToken.None));
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

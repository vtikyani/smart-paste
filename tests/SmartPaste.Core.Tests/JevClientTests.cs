using System.Net;
using System.Text.Json;
using SmartPaste.Core;

public class JevClientTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string Answer = "{\"answers\":{\"field_value\":{\"type\":\"choice\",\"choice\":\"zip\",\"confidence\":0.97,\"probabilities\":{\"zip\":0.98,\"none\":0.02}}}}";

    private static JevQuestion Question()
    {
        var profile = new Profile([new Entity("person", new Dictionary<string, string> { [FieldKeys.Zip] = "07081" })], DateTimeOffset.UtcNow);
        return JevQuestion.ForField(new FieldContext("ZIP code", null, null, null, "Edit", false, []), profile);
    }

    [Fact]
    public async Task TypeSafeRequestShape()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Answer);
        var choice = await new JevClient(http.Client(), new JevCredentials(" ts-key ", null)).ChooseAsync(Question(), Deadline, CancellationToken.None);

        Assert.Equal(JevEndpoint.TypeSafe, choice.Route);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", request.Url);
        Assert.Equal("Bearer ts-key", request.Authorization);
        Assert.False(request.Headers.ContainsKey("HTTP-Referer"));
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("jev-latest", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("state").ValueKind);
        var question = body.RootElement.GetProperty("questions").GetProperty("field_value");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.True(question.GetProperty("criteria").TryGetProperty("none", out _));
    }

    [Fact]
    public async Task OpenRouterRequestShapeWithAttribution()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Answer);
        var choice = await new JevClient(http.Client(), new JevCredentials(null, "or-key")).ChooseAsync(Question(), Deadline, CancellationToken.None);

        Assert.Equal(JevEndpoint.OpenRouter, choice.Route);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://openrouter.ai/api/alpha/decisions", request.Url);
        Assert.Equal("Bearer or-key", request.Authorization);
        Assert.Equal("https://github.com/vtikyani/smart-paste", request.Headers["HTTP-Referer"]);
        Assert.Equal("Smart Paste", request.Headers["X-OpenRouter-Title"]);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("typesafe/jev-1.13", body.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ServiceFailure.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired, ServiceFailure.PaymentRequired)]
    [InlineData(HttpStatusCode.InternalServerError, ServiceFailure.ServerError)]
    public async Task AutoFallsThroughToOpenRouterAndReportsTheFirstRoute(HttpStatusCode typeSafeStatus, ServiceFailure expected)
    {
        var http = FakeHttp.Reply(request => request.RequestUri!.Host == "api.typesafe.ai"
            ? FakeHttp.Response(typeSafeStatus, "{\"error\":{\"code\":1,\"message\":\"no\"}}")
            : FakeHttp.Response(HttpStatusCode.OK, Answer));
        var choice = await new JevClient(http.Client(), new JevCredentials("ts", "or")).ChooseAsync(Question(), Deadline, CancellationToken.None);

        Assert.Equal(JevEndpoint.OpenRouter, choice.Route);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(expected, choice.FailedRoutes[JevEndpoint.TypeSafe]);
    }

    [Fact]
    public async Task PreferenceLimitsTheRoute()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Answer);
        var client = new JevClient(http.Client(), new JevCredentials("ts", "or", JevRoutePreference.OpenRouter));
        Assert.Equal(JevEndpoint.OpenRouter, (await client.ChooseAsync(Question(), Deadline, CancellationToken.None)).Route);
        Assert.Equal([JevEndpoint.TypeSafe], new JevCredentials("ts", "or", JevRoutePreference.TypeSafe).Routes);
        Assert.Empty(new JevCredentials(null, "or", JevRoutePreference.TypeSafe).Routes);
    }

    [Fact]
    public async Task AllRoutesFailingReportsTheMostActionableFailure()
    {
        var http = FakeHttp.Reply(request => request.RequestUri!.Host == "api.typesafe.ai"
            ? throw new HttpRequestException("offline")
            : FakeHttp.Response(HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new JevClient(http.Client(), new JevCredentials("ts", "or")).ChooseAsync(Question(), Deadline, CancellationToken.None));
        Assert.Equal(ServiceFailure.Unauthorized, error.Failure);
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task NoKeyNeverCallsTheNetwork()
    {
        var http = FakeHttp.Reply(_ => throw new InvalidOperationException("must not be called"));
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new JevClient(http.Client(), new JevCredentials(" ", null)).ChooseAsync(Question(), Deadline, CancellationToken.None));
        Assert.Equal(ServiceFailure.NoKey, error.Failure);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task KeyWithSpacesIsRejectedWithoutSending()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Answer);
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new JevClient(http.Client(), new JevCredentials("two words", null)).ChooseAsync(Question(), Deadline, CancellationToken.None));
        Assert.Equal(ServiceFailure.Unauthorized, error.Failure);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task SharedDeadlineBecomesTimeout()
    {
        var http = new FakeHttp(async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttp.Response(HttpStatusCode.OK, Answer);
        });
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new JevClient(http.Client(), new JevCredentials("ts", "or")).ChooseAsync(Question(), TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.Equal(ServiceFailure.Timeout, error.Failure);
        // The deadline is shared: once spent, the second route is not tried.
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cancel = new CancellationTokenSource();
        var http = new FakeHttp(async (_, _, token) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttp.Response(HttpStatusCode.OK, Answer);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JevClient(http.Client(), new JevCredentials("ts", "or")).ChooseAsync(Question(), Deadline, cancel.Token));
    }

    [Fact]
    public async Task UnparseableBodyFallsThroughThenReportsBadResponse()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, "<html>oops</html>");
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new JevClient(http.Client(), new JevCredentials("ts", "or")).ChooseAsync(Question(), Deadline, CancellationToken.None));
        Assert.Equal(ServiceFailure.BadResponse, error.Failure);
        Assert.Equal(2, http.Requests.Count);
        Assert.DoesNotContain("oops", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialsNeverPrintKeys()
    {
        var text = new JevCredentials("secret-ts", "secret-or").ToString();
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("set", text, StringComparison.Ordinal);
    }
}

using System.Net;
using SmartPaste.Core;

public class ConnectionChecksTests
{
    private const string GoodKey = "{\"data\":{\"label\":\"sk-or-v1-abc...\",\"limit\":null,\"limit_remaining\":null,\"usage\":0.25,\"is_free_tier\":false,\"is_management_key\":false,\"budgets\":[],\"effective_budget\":null}}";

    [Fact]
    public async Task OpenRouterKeyWorks()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, GoodKey);
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(http.Client(), " or-key ", CancellationToken.None);

        Assert.Equal(new KeyCheckResult(true, null, "Key works."), result);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://openrouter.ai/api/v1/key", request.Url);
        Assert.Equal("Bearer or-key", request.Authorization);
    }

    [Theory]
    [InlineData("{\"data\":{\"limit\":5,\"limit_remaining\":0}}")]
    [InlineData("{\"data\":{\"limit\":5,\"limit_remaining\":-0.5}}")]
    [InlineData("{\"data\":{\"limit_remaining\":null,\"effective_budget\":{\"scope\":\"workspace\",\"limit_usd\":10,\"spend_usd\":10,\"remaining_usd\":0}}}")]
    public async Task OpenRouterKeyWithNothingLeftToSpend(string body)
    {
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), "k", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal(ServiceFailure.PaymentRequired, result.Failure);
        Assert.Equal("This key has no credits left.", result.Message);
    }

    [Fact]
    public async Task OpenRouterManagementKeyCannotRunModels()
    {
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(
            FakeHttp.Reply(HttpStatusCode.OK, "{\"data\":{\"is_management_key\":true}}").Client(), "k", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal(ServiceFailure.Unauthorized, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ServiceFailure.Unauthorized, "OpenRouter rejected this key.")]
    [InlineData(HttpStatusCode.PaymentRequired, ServiceFailure.PaymentRequired, "This key has no credits left.")]
    [InlineData(HttpStatusCode.TooManyRequests, ServiceFailure.RateLimited, "OpenRouter is busy with too many requests. Try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, ServiceFailure.ServerError, "OpenRouter is having trouble right now. Try again later.")]
    public async Task OpenRouterFailures(HttpStatusCode status, ServiceFailure failure, string message)
    {
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(status).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, failure, message), result);
    }

    [Theory]
    [InlineData("<html/>")]
    [InlineData("[]")]
    [InlineData("\"ok\"")]
    [InlineData("{\"data\":[]}")]
    public async Task OpenRouterOddBodyIsBadResponse(string body)
    {
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.BadResponse, "OpenRouter sent an answer Smart Paste could not read. Try again later."), result);
    }

    [Fact]
    public async Task OpenRouterValuesOfAnotherKindReadAsAbsent()
    {
        const string body = "{\"data\":{\"is_management_key\":\"yes\",\"limit_remaining\":\"lots\",\"effective_budget\":[1]}}";
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(true, null, "Key works."), result);
    }

    [Fact]
    public async Task UnknownCharsetIsBadResponseNotACrash()
    {
        var http = FakeHttp.Reply(_ => FakeHttp.ResponseWithCharset(GoodKey, "no-such-charset"));
        var result = await ConnectionChecks.CheckOpenRouterKeyAsync(http.Client(), "k", CancellationToken.None);
        Assert.Equal(ServiceFailure.BadResponse, result.Failure);
    }

    [Fact]
    public async Task ForbiddenSaysTheKeyIsNotAllowedRatherThanRejected()
    {
        const string message = "This key isn't allowed to make this request.";
        var openRouter = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(HttpStatusCode.Forbidden).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.Unauthorized, message), openRouter);
        var typeSafe = await ConnectionChecks.CheckTypeSafeKeyAsync(FakeHttp.Reply(HttpStatusCode.Forbidden).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.Unauthorized, message), typeSafe);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":402,\"message\":\"Insufficient credits\"}}", ServiceFailure.PaymentRequired, "This key has no credits left.")]
    [InlineData("{\"error\":{\"code\":401,\"message\":\"User not found\"}}", ServiceFailure.Unauthorized, "{0} rejected this key.")]
    [InlineData("{\"error\":{\"code\":429,\"message\":\"Slow down\"}}", ServiceFailure.RateLimited, "{0} is busy with too many requests. Try again in a minute.")]
    public async Task ErrorEnvelopeSentWithHttp200IsTheFailureItNames(string body, ServiceFailure failure, string message)
    {
        var openRouter = await ConnectionChecks.CheckOpenRouterKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, failure, string.Format(message, "OpenRouter")), openRouter);
        var typeSafe = await ConnectionChecks.CheckTypeSafeKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, failure, string.Format(message, "TypeSafe")), typeSafe);
    }

    [Fact]
    public async Task TypeSafeKeyWorksWhateverTheSuccessBody()
    {
        // Only the status matters for this check, so a body it cannot parse is not a failure.
        var result = await ConnectionChecks.CheckTypeSafeKeyAsync(FakeHttp.Reply(HttpStatusCode.OK, "[]").Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(true, null, "Key works."), result);
    }

    [Fact]
    public async Task TypeSafeKeyWorks()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, "{\"models\":[{\"name\":\"jev-latest\"}]}");
        var result = await ConnectionChecks.CheckTypeSafeKeyAsync(http.Client(), "ts-key", CancellationToken.None);

        Assert.Equal(new KeyCheckResult(true, null, "Key works."), result);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.typesafe.ai/v1/models", request.Url);
        Assert.Equal("Bearer ts-key", request.Authorization);
    }

    [Fact]
    public async Task TypeSafeRejectedKey()
    {
        var body = "{\"detail\":{\"error_type\":\"authentication_error\",\"message\":\"Cannot authenticate with the server.\"}}";
        var result = await ConnectionChecks.CheckTypeSafeKeyAsync(FakeHttp.Reply(HttpStatusCode.Unauthorized, body).Client(), "bad", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.Unauthorized, "TypeSafe rejected this key."), result);
    }

    [Fact]
    public async Task OfflineAndEmptyKey()
    {
        var offline = await ConnectionChecks.CheckTypeSafeKeyAsync(FakeHttp.Reply(_ => throw new HttpRequestException("dns")).Client(), "k", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.Network, "Can't reach TypeSafe. Check your internet connection."), offline);

        var http = FakeHttp.Reply(HttpStatusCode.OK);
        var empty = await ConnectionChecks.CheckOpenRouterKeyAsync(http.Client(), "", CancellationToken.None);
        Assert.Equal(new KeyCheckResult(false, ServiceFailure.NoKey, "Enter a key first."), empty);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var http = new FakeHttp(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return FakeHttp.Response(HttpStatusCode.OK); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectionChecks.CheckTypeSafeKeyAsync(http.Client(), "k", cancel.Token));
    }
}

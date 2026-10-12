using System.Net;
using System.Text.Json;
using SmartPaste.Core;

public class OpenRouterParserTests
{
    private const string Text = "Ada Lovelace, ada@example.com, 201-555-0100";

    private static string Reply(string content, string finish = "stop") =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finish } } });

    private const string Entities = "{\"entities\":[{\"label\":\"person\",\"fields\":[{\"key\":\"first_name\",\"value\":\"Ada\"},{\"key\":\"last_name\",\"value\":\"Lovelace\"},{\"key\":\"email\",\"value\":\"ada@example.com\"},{\"key\":\"phone\",\"value\":\"555-867-5309\"}]}]}";

    [Fact]
    public async Task RequestShapeForClaudeHaiku()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        await new OpenRouterParser(http.Client(), "test-key").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None);

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.Url);
        Assert.Equal("Bearer test-key", request.Authorization);
        Assert.Equal("https://github.com/vtikyani/smart-paste", request.Headers["HTTP-Referer"]);
        Assert.Equal("Smart Paste", request.Headers["X-OpenRouter-Title"]);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("anthropic/claude-haiku-5.5", root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("json_schema").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.True(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        Assert.Equal("latency", root.GetProperty("provider").GetProperty("sort").GetString());
        var messages = root.GetProperty("messages");
        Assert.Equal(OpenRouterParser.SystemPrompt, messages[0].GetProperty("content").GetString());
        Assert.Equal(Text, messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ModelIsConfigurable()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        var parser = new OpenRouterParser(http.Client(), "k", "openai/gpt-6-mini");
        await parser.ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal("openai/gpt-6-mini", parser.Model);
        Assert.Contains("\"model\":\"openai/gpt-6-mini\"", http.Requests[0].Body);
    }

    [Fact]
    public async Task ReplyIsGroundedInTheCopiedText()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        var profile = await new OpenRouterParser(http.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal("Ada Lovelace", profile.ValuesFor(FieldKeys.FullName).Single().Value);
        Assert.Equal("ada@example.com", profile.ValuesFor(FieldKeys.Email).Single().Value);
        // The model's phone number is not in the text, so it is dropped.
        Assert.Empty(profile.ValuesFor(FieldKeys.Phone));
    }

    private static string SentText(FakeHttp http)
    {
        using var body = JsonDocument.Parse(Assert.Single(http.Requests).Body!);
        return body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    }

    [Fact]
    public async Task SecretsNeverLeaveThePc()
    {
        const string text = "Jordan Rivera, jordan@example.com, 201-555-0100\nRouting number: 021000021\nAccount number: 123456789\n" +
                            "Paid with 4111 1111 1111 1111, SSN 123-45-6789\nIBAN DE89 3704 0044 0532 0130 00\nPassword\nhunter2";
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        await new OpenRouterParser(http.Client(), "k").ParseTextAsync(text, DateTimeOffset.UtcNow, CancellationToken.None);

        var sent = SentText(http);
        Assert.Contains("Jordan Rivera, jordan@example.com, 201-555-0100", sent, StringComparison.Ordinal);
        Assert.Contains("Paid with " + Redaction.Mask + ", SSN " + Redaction.Mask, sent, StringComparison.Ordinal);
        foreach (var secret in new[] { "021000021", "123456789", "4111", "123-45-6789", "DE89", "3704", "Routing", "Account number", "hunter2" })
            Assert.DoesNotContain(secret, sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BankDetailsTheModelReturnsAreDropped()
    {
        const string text = "Jordan Rivera, jordan@example.com\nRouting number: 021000021 / Account number: 123456789";
        const string reply = "{\"entities\":[{\"label\":\"person\",\"fields\":[{\"key\":\"full_name\",\"value\":\"Jordan Rivera\"}," +
                             "{\"key\":\"email\",\"value\":\"jordan@example.com\"},{\"key\":\"account_number\",\"value\":\"123456789\"}," +
                             "{\"key\":\"routing_number\",\"value\":\"021000021\"}]}]}";
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(reply));
        var profile = await new OpenRouterParser(http.Client(), "k").ParseTextAsync(text, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(profile.ValuesFor("account_number"));
        Assert.Empty(profile.ValuesFor("routing_number"));
        Assert.Equal("jordan@example.com", profile.ValuesFor(FieldKeys.Email).Single().Value);
        Assert.DoesNotContain("021000021", SentText(http), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMaskNeverComesBackAsAValue()
    {
        var reply = "{\"entities\":[{\"label\":\"person\",\"fields\":{\"email\":\"jordan@example.com\",\"note\":\"" + Redaction.Mask + "\"}}]}";
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(reply));
        var profile = await new OpenRouterParser(http.Client(), "k")
            .ParseTextAsync("jordan@example.com, card 4111 1111 1111 1111", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(profile.ValuesFor("note"));
        Assert.Single(profile.ValuesFor(FieldKeys.Email));
    }

    [Fact]
    public async Task TextOfOnlySecretsIsNotSent()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        var profile = await new OpenRouterParser(http.Client(), "k")
            .ParseTextAsync("Card number: 4111 1111 1111 1111\nCVV: 123\n4111-1111-1111-1111", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(http.Requests);
        Assert.Empty(profile.Entities);
    }

    [Fact]
    public void ThePromptNamesEverySecret()
    {
        foreach (var term in new[] { "passwords", "PINs", "card numbers", "CVV", "bank account", "routing", "IBAN", "social security", "one-time", "security questions" })
            Assert.Contains(term, OpenRouterParser.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObjectShapedFieldsInProseStillParse()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply("Sure! {\"entities\":[{\"label\":\"person\",\"fields\":{\"email\":\"ada@example.com\"}}]}"));
        var profile = await new OpenRouterParser(http.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal("ada@example.com", profile.ValuesFor(FieldKeys.Email).Single().Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ServiceFailure.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired, ServiceFailure.PaymentRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, ServiceFailure.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, ServiceFailure.ServerError)]
    [InlineData(HttpStatusCode.BadRequest, ServiceFailure.BadResponse)]
    public async Task HttpErrorsAreTyped(HttpStatusCode status, ServiceFailure expected)
    {
        var http = FakeHttp.Reply(status, "{\"error\":{\"code\":1,\"message\":\"Ada Lovelace\"}}");
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(http.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(expected, error.Failure);
        Assert.Equal((int)status, error.StatusCode);
        Assert.DoesNotContain("Ada", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"error\":{\"code\":\"oops\",\"message\":\"x\"}}")]
    public async Task UnusableRepliesAreBadResponses(string body)
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, body);
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(http.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(ServiceFailure.BadResponse, error.Failure);
    }

    [Fact]
    public async Task ErrorObjectWithStatusIsTyped()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, "{\"error\":{\"code\":402,\"message\":\"Insufficient credits\"}}");
        var error = await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(http.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(ServiceFailure.PaymentRequired, error.Failure);
    }

    [Fact]
    public async Task TruncatedOrNonJsonContentIsBadResponse()
    {
        var cut = FakeHttp.Reply(HttpStatusCode.OK, Reply("{\"entities\":[{\"label\":\"pers", "length"));
        Assert.Equal(ServiceFailure.BadResponse, (await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(cut.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None))).Failure);
        var prose = FakeHttp.Reply(HttpStatusCode.OK, Reply("I cannot help with that."));
        Assert.Equal(ServiceFailure.BadResponse, (await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(prose.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None))).Failure);
    }

    [Fact]
    public async Task NetworkAndTimeoutAreTyped()
    {
        var offline = FakeHttp.Reply(_ => throw new HttpRequestException("no route"));
        Assert.Equal(ServiceFailure.Network, (await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(offline.Client(), "k").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None))).Failure);

        var slow = new FakeHttp(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return FakeHttp.Response(HttpStatusCode.OK); });
        Assert.Equal(ServiceFailure.Timeout, (await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(slow.Client(), "k", timeout: TimeSpan.FromMilliseconds(100)).ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None))).Failure);
    }

    [Fact]
    public async Task InputLimitsAndMissingKey()
    {
        var http = FakeHttp.Reply(HttpStatusCode.OK, Reply(Entities));
        var parser = new OpenRouterParser(http.Client(), "k");
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseTextAsync(new string('x', TextHeuristics.MaxChars + 1), DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Empty(http.Requests);

        var noKey = await Assert.ThrowsAsync<ServiceException>(() =>
            new OpenRouterParser(http.Client(), " ").ParseTextAsync(Text, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(ServiceFailure.NoKey, noKey.Failure);
        Assert.Empty(http.Requests);
    }
}

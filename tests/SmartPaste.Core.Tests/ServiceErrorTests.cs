using SmartPaste.Core;

public class ServiceErrorTests
{
    [Theory]
    [InlineData(401, ServiceFailure.Unauthorized)]
    [InlineData(403, ServiceFailure.Unauthorized)]
    [InlineData(402, ServiceFailure.PaymentRequired)]
    [InlineData(429, ServiceFailure.RateLimited)]
    [InlineData(408, ServiceFailure.Timeout)]
    [InlineData(500, ServiceFailure.ServerError)]
    [InlineData(502, ServiceFailure.ServerError)]
    [InlineData(503, ServiceFailure.ServerError)]
    [InlineData(524, ServiceFailure.ServerError)]
    [InlineData(529, ServiceFailure.ServerError)]
    [InlineData(400, ServiceFailure.BadResponse)]
    [InlineData(404, ServiceFailure.BadResponse)]
    [InlineData(413, ServiceFailure.BadResponse)]
    [InlineData(422, ServiceFailure.BadResponse)]
    public void StatusCodesMapToFailures(int status, ServiceFailure expected) =>
        Assert.Equal(expected, ServiceException.FailureForStatus(status));

    [Fact]
    public void MostActionableFailureRanksFirst()
    {
        var ranked = Enum.GetValues<ServiceFailure>().OrderByDescending(ServiceException.Actionability).ToArray();
        Assert.Equal(ServiceFailure.Unauthorized, ranked[0]);
        Assert.Equal(ServiceFailure.PaymentRequired, ranked[1]);
        Assert.Equal(ServiceFailure.NoKey, ranked[^1]);
        Assert.True(ServiceException.Actionability(ServiceFailure.Unauthorized) > ServiceException.Actionability(ServiceFailure.Network));
    }

    [Fact]
    public void MessageIsFixedTextWithStatus()
    {
        var error = new ServiceException(ServiceFailure.RateLimited, 429);
        Assert.Equal(ServiceFailure.RateLimited, error.Failure);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal("The service is rate limiting requests. (HTTP 429)", error.Message);
        Assert.Equal("The service does not allow this API key to make this request. (HTTP 403)",
            new ServiceException(ServiceFailure.Unauthorized, 403).Message);
    }

    [Fact]
    public void ReadJsonTurnsEveryMalformedBodyIntoBadResponse()
    {
        // Each reader assumes a shape the body does not have; JsonElement throws InvalidOperationException for that.
        Assert.Equal(ServiceFailure.BadResponse, Read("[]", root => root.GetProperty("data").GetString()));
        Assert.Equal(ServiceFailure.BadResponse, Read("{\"choices\":[\"x\"]}", root => root.GetProperty("choices")[0].GetProperty("message").GetString()));
        Assert.Equal(ServiceFailure.BadResponse, Read("{\"n\":\"x\"}", root => root.GetProperty("n").GetInt32().ToString()));
        Assert.Equal(ServiceFailure.BadResponse, Read("{\"a\":[]}", root => root.GetProperty("a")[3].GetString()));
        Assert.Equal(ServiceFailure.BadResponse, Read("not json", root => root.GetString()));
        Assert.Equal(ServiceFailure.BadResponse, Read("", root => root.GetString()));
        // A ServiceException the reader throws is kept as it is.
        Assert.Equal(ServiceFailure.RateLimited, Read("{}", _ => throw new ServiceException(ServiceFailure.RateLimited)));
        Assert.Equal("x", ServiceHttp.ReadJson("{\"a\":\"x\"}", root => root.GetProperty("a").GetString()));

        static ServiceFailure Read(string body, Func<System.Text.Json.JsonElement, string?> read) =>
            Assert.Throws<ServiceException>(() => ServiceHttp.ReadJson(body, read)).Failure;
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":402,\"message\":\"Out of balance zq7\"}}", ServiceFailure.PaymentRequired, 402)]
    [InlineData("{\"error\":{\"code\":\"401\"}}", ServiceFailure.Unauthorized, 401)]
    [InlineData("{\"error\":{\"status\":429}}", ServiceFailure.RateLimited, 429)]
    [InlineData("{\"error\":{\"code\":7}}", ServiceFailure.BadResponse, null)]
    [InlineData("{\"error\":{\"code\":\"rate_limited\"}}", ServiceFailure.BadResponse, null)]
    public void ErrorEnvelopesMapLikeHttpStatuses(string body, ServiceFailure failure, int? status)
    {
        var error = ServiceHttp.ErrorIn(body);
        Assert.NotNull(error);
        Assert.Equal(failure, error.Failure);
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("zq7", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"answers\":{}}")]
    [InlineData("{\"error\":null}")]
    [InlineData("{\"error\":\"text\"}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void OtherBodiesAreNotErrorEnvelopes(string body) => Assert.Null(ServiceHttp.ErrorIn(body));

    // A code whose escape is not valid text ("\ud800") makes JsonElement.GetString throw; it is simply no usable code.
    [Fact]
    public void AnUndecodableCodeIsAnEnvelopeWithoutACode()
    {
        const string body = "{\"error\":{\"code\":\"\\ud800\"}}";
        var error = ServiceHttp.ErrorIn(body);
        Assert.NotNull(error);
        Assert.Equal(ServiceFailure.BadResponse, error.Failure);
        Assert.Null(error.StatusCode);
        Assert.Equal(ServiceFailure.BadResponse,
            Assert.Throws<ServiceException>(() => ServiceHttp.ReadJson(body, root => root.GetRawText())).Failure);
    }

    // A .NET string that is not valid UTF-16 (a lone surrogate) cannot even be parsed: that is an unusable body too.
    [Fact]
    public void TextThatIsNotValidUtf16IsABadResponse()
    {
        const string body = "{\"a\":\"\uD800\"}";
        Assert.Equal(ServiceFailure.BadResponse,
            Assert.Throws<ServiceException>(() => ServiceHttp.ReadJson(body, root => root.GetRawText())).Failure);
        Assert.Null(ServiceHttp.ErrorIn(body));
    }

    [Theory]
    [InlineData("{\"code\":503}", ServiceFailure.ServerError, 503)]
    [InlineData("{\"code\":\"402\"}", ServiceFailure.PaymentRequired, 402)]
    [InlineData("{\"message\":\"no code\"}", ServiceFailure.BadResponse, null)]
    public void AnErrorObjectAnywhereMapsLikeAnHttpStatus(string json, ServiceFailure failure, int? status)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var error = ServiceHttp.Envelope(document.RootElement);
        Assert.NotNull(error);
        Assert.Equal(failure, error.Failure);
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("\"a bare message\"")]
    public void OnlyAnObjectIsAnErrorObject(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Null(ServiceHttp.Envelope(document.RootElement));
    }
}

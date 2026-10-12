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
    }
}

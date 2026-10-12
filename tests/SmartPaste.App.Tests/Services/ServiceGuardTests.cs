using SmartPaste.App.Services;
using SmartPaste.Core;

namespace SmartPaste.App.Tests.Services;

public sealed class ServiceGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ServiceFailure.Unauthorized, ServiceStatus.KeyRejected)]
    [InlineData(ServiceFailure.PaymentRequired, ServiceStatus.OutOfCredits)]
    [InlineData(ServiceFailure.RateLimited, ServiceStatus.RateLimited)]
    [InlineData(ServiceFailure.Network, ServiceStatus.Unreachable)]
    [InlineData(ServiceFailure.ServerError, ServiceStatus.Unreachable)]
    [InlineData(ServiceFailure.BadResponse, ServiceStatus.ServiceError)]
    [InlineData(ServiceFailure.NoKey, ServiceStatus.NotConfigured)]
    public void FailuresMapToWhatTheUserSees(ServiceFailure failure, ServiceStatus status) =>
        Assert.Equal(status, ServiceGuard.StatusFor(failure));

    [Fact]
    public void RejectedKeyHoldsCallsBackUntilKeysChange()
    {
        var guard = new ServiceGuard(ServiceStatus.Ready);
        Assert.True(guard.Failed(ServiceFailure.Unauthorized, Now));
        Assert.False(guard.CanCall(Now.AddMinutes(5)));
        Assert.True(guard.Reset(hasKey: true));
        Assert.True(guard.CanCall(Now.AddMinutes(5)));
        Assert.Equal(ServiceStatus.Ready, guard.Status);
    }

    [Fact]
    public void LostConnectionPausesBrieflyThenRetries()
    {
        var guard = new ServiceGuard(ServiceStatus.Ready);
        guard.Failed(ServiceFailure.Network, Now);
        Assert.False(guard.CanCall(Now.AddSeconds(10)));
        Assert.True(guard.CanCall(Now.AddSeconds(21)));
    }

    [Fact]
    public void TimeoutNeverPausesCalls()
    {
        var guard = new ServiceGuard(ServiceStatus.Ready);
        guard.Failed(ServiceFailure.Timeout, Now);
        Assert.True(guard.CanCall(Now));
    }

    [Fact]
    public void ReportsOnlyRealChanges()
    {
        var guard = new ServiceGuard(ServiceStatus.Ready);
        Assert.False(guard.Succeeded());
        Assert.True(guard.Failed(ServiceFailure.RateLimited, Now));
        Assert.False(guard.Failed(ServiceFailure.RateLimited, Now));
        Assert.True(guard.Succeeded());
    }

    [Fact]
    public void RemovedKeyMeansNotConfigured()
    {
        var guard = new ServiceGuard(ServiceStatus.Ready);
        guard.Reset(hasKey: false);
        Assert.Equal(ServiceStatus.NotConfigured, guard.Status);
    }
}

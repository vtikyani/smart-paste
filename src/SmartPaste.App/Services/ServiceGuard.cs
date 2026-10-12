using SmartPaste.Core;

namespace SmartPaste.App.Services;

// Remembers the last failure of one cloud service (parsing on OpenRouter, or Jev) and holds calls back while another
// call could not work: a rejected key, no credits, a rate limit, or no connection. Meanwhile Smart Paste uses its
// local rules at once instead of waiting for a call that would fail. A new key resets it. Thread-safe.
internal sealed class ServiceGuard
{
    private readonly object _gate = new();
    private DateTimeOffset _blockedUntil;

    public ServiceGuard(ServiceStatus initial) => Status = initial;

    public ServiceStatus Status { get; private set; }

    public bool CanCall(DateTimeOffset now)
    {
        lock (_gate) return now >= _blockedUntil;
    }

    // Returns true when the status changed.
    public bool Succeeded()
    {
        lock (_gate)
        {
            _blockedUntil = default;
            return Set(ServiceStatus.Ready);
        }
    }

    // Returns true when the status changed.
    public bool Failed(ServiceFailure failure, DateTimeOffset now)
    {
        lock (_gate)
        {
            _blockedUntil = now + Pause(failure);
            return Set(StatusFor(failure));
        }
    }

    // Keys were added, changed, or removed: forget the old failure.
    public bool Reset(bool hasKey)
    {
        lock (_gate)
        {
            _blockedUntil = default;
            return Set(hasKey ? ServiceStatus.Ready : ServiceStatus.NotConfigured);
        }
    }

    public static ServiceStatus StatusFor(ServiceFailure failure) => failure switch
    {
        ServiceFailure.NoKey => ServiceStatus.NotConfigured,
        ServiceFailure.Unauthorized => ServiceStatus.KeyRejected,
        ServiceFailure.PaymentRequired => ServiceStatus.OutOfCredits,
        ServiceFailure.RateLimited => ServiceStatus.RateLimited,
        ServiceFailure.BadResponse => ServiceStatus.ServiceError,
        _ => ServiceStatus.Unreachable
    };

    // How long to hold calls back. A rejected key will not start working by itself (a new key resets the guard);
    // a timeout is often a one-off, so it does not pause at all.
    private static TimeSpan Pause(ServiceFailure failure) => failure switch
    {
        ServiceFailure.Unauthorized => TimeSpan.FromMinutes(30),
        ServiceFailure.PaymentRequired => TimeSpan.FromMinutes(10),
        ServiceFailure.RateLimited => TimeSpan.FromSeconds(30),
        ServiceFailure.Network or ServiceFailure.ServerError => TimeSpan.FromSeconds(20),
        ServiceFailure.BadResponse => TimeSpan.FromMinutes(1),
        _ => TimeSpan.Zero
    };

    private bool Set(ServiceStatus status)
    {
        if (Status == status) return false;
        Status = status;
        return true;
    }
}

using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// Holds the armed block. Every copy starts a new generation: whatever was armed is dropped at once, and work
// still running for an older copy (a slow AI parse) can no longer arm anything. Thread-safe; mirrors the
// armed block into AppState on the UI thread.
internal sealed class ProfileStore : IDisposable
{
    private readonly object _gate = new();
    private readonly AppState _state;
    private readonly SettingsStore _settings;
    private readonly Dispatcher _dispatcher;
    private ArmedProfile? _armed;
    private int _generation;
    private CancellationTokenSource _work = new();

    public ProfileStore(AppState state, SettingsStore settings, Dispatcher dispatcher)
    {
        _state = state;
        _settings = settings;
        _dispatcher = dispatcher;
    }

    // Raised on any thread after the armed block changes (armed, replaced, cleared).
    public event Action? Changed;

    // The armed block, or null when nothing is armed or it has expired.
    public ArmedProfile? Current
    {
        get
        {
            lock (_gate)
                return _armed is { } armed && armed.ExpiresAt > DateTimeOffset.Now ? armed : null;
        }
    }

    public bool HasArmed => Current is not null;

    // A new copy happened: drop the armed block and cancel work for the previous copy. Returns the generation
    // and a token that is canceled as soon as another copy arrives.
    public (int Generation, CancellationToken Token) BeginCopy()
    {
        CancellationTokenSource previous;
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            previous = _work;
            _work = new CancellationTokenSource();
            token = _work.Token;
            generation = ++_generation;
            _armed = null;
        }
        previous.Cancel();
        previous.Dispose();
        Publish();
        return (generation, token);
    }

    // Arms a profile for the given copy. Returns false (and arms nothing) when a newer copy has happened since.
    public bool Arm(int generation, Profile profile, ProfileOrigin origin, bool parsedByAi)
    {
        if (!profile.IsMultiField) return false;
        lock (_gate)
        {
            if (generation != _generation) return false;
            var now = DateTimeOffset.Now;
            // A richer parse of the same copy keeps the original arming time, so the countdown does not reset.
            var armedAt = _armed?.ArmedAt ?? now;
            _armed = new ArmedProfile(profile, armedAt, armedAt + _settings.Current.ProfileLifetime, origin, parsedByAi);
        }
        Publish();
        return true;
    }

    // Is this still the newest copy?
    public bool IsCurrent(int generation)
    {
        lock (_gate) return generation == _generation;
    }

    // Drops the armed block (Clear in the tray, Smart Paste turned off, or the block expired).
    public void Clear() => BeginCopy();

    private void Publish()
    {
        Changed?.Invoke();
        var armed = Current;
        if (_dispatcher.CheckAccess()) _state.Armed = armed;
        else _dispatcher.BeginInvoke(() => _state.Armed = Current);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _work.Cancel();
            _work.Dispose();
            _armed = null;
        }
    }
}

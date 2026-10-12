using System.Net.Http;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// Decides what a field gets: Core's FieldDecider over the saved keys and the learned labels. Jev decides every paste
// it can; while Jev cannot be reached (no key, a rejected key, no connection) the local label list decides instead.
// Keeps AppState.JevStatus current. Thread-safe.
internal sealed class DecisionService : IDisposable
{
    // A single slow answer is normal; this many in a row means Jev is effectively unreachable.
    private const int TimeoutsBeforeUnreachable = 3;

    private readonly HttpClient _http;
    private readonly SettingsStore _settings;
    private readonly AppState _state;
    private readonly Dispatcher _dispatcher;
    private readonly ServiceGuard _guard;
    private int _timeoutsInARow;

    public DecisionService(HttpClient http, LabelCache labels, SettingsStore settings, AppState state, Dispatcher dispatcher)
    {
        _http = http;
        Labels = labels;
        _settings = settings;
        _state = state;
        _dispatcher = dispatcher;
        _guard = new ServiceGuard(Credentials().HasKey ? ServiceStatus.Ready : ServiceStatus.NotConfigured);
        CredentialStore.Changed += OnKeysChanged;
        _settings.Changed += OnSettingsChanged;
        Publish();
    }

    public LabelCache Labels { get; }

    // allowJev false decides from the local label list only (field hints while "Ask Jev ahead" is off).
    public async Task<Decision> DecideAsync(FieldContext field, Profile profile, TimeSpan jevDeadline, CancellationToken ct, bool allowJev = true)
    {
        var credentials = Credentials();
        var useJev = allowJev && credentials.HasKey && _guard.CanCall(DateTimeOffset.Now);
        var decider = new FieldDecider(Labels, useJev ? new JevClient(_http, credentials) : null);
        var decision = await decider.DecideAsync(field, profile, jevDeadline, ct).ConfigureAwait(false);
        if (useJev) Record(decision);
        return decision;
    }

    // The user chose a value in the picker: the label is learned for the offline fallback.
    public void LearnChoice(FieldContext field, Candidate chosen) => new FieldDecider(Labels, null).LearnChoice(field, chosen);

    private void Record(Decision decision)
    {
        bool changed;
        switch (decision.JevFailure)
        {
            case ServiceFailure.Timeout:
                changed = Interlocked.Increment(ref _timeoutsInARow) >= TimeoutsBeforeUnreachable &&
                          _guard.Failed(ServiceFailure.Network, DateTimeOffset.Now);
                break;
            case { } failure:
                Interlocked.Exchange(ref _timeoutsInARow, 0);
                changed = _guard.Failed(failure, DateTimeOffset.Now);
                Log.Event("jev_unavailable", ("reason", failure.ToString()));
                break;
            default:
                // Jev answered (a value, a choice to make, or "none").
                Interlocked.Exchange(ref _timeoutsInARow, 0);
                changed = decision.DecidedBy == DecisionSource.Jev && _guard.Succeeded();
                break;
        }
        if (changed) Publish();
    }

    private JevCredentials Credentials() => new(
        CredentialStore.Get(CredentialStore.TypeSafe),
        CredentialStore.Get(CredentialStore.OpenRouter),
        _settings.Current.JevRoute switch
        {
            JevRoute.TypeSafe => JevRoutePreference.TypeSafe,
            JevRoute.OpenRouter => JevRoutePreference.OpenRouter,
            _ => JevRoutePreference.Auto
        });

    private void OnKeysChanged(string target)
    {
        Interlocked.Exchange(ref _timeoutsInARow, 0);
        if (_guard.Reset(Credentials().HasKey)) Publish();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.JevRoute != current.JevRoute && _guard.Reset(Credentials().HasKey)) Publish();
    }

    private void Publish()
    {
        if (_dispatcher.CheckAccess()) _state.JevStatus = _guard.Status;
        else _dispatcher.BeginInvoke(() => _state.JevStatus = _guard.Status);
    }

    public void Dispose()
    {
        CredentialStore.Changed -= OnKeysChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}

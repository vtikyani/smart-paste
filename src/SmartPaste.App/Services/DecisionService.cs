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
    private readonly object _routeGate = new();
    // A route that failed while another one answered (Automatic with both keys): skipped until it may work again, so
    // questions stop going to a service that turns them away and the answer comes faster.
    private readonly Dictionary<JevEndpoint, DateTimeOffset> _routeBlockedUntil = [];
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
        IJevChooser? jev = useJev ? new RouteWatchingChooser(new JevClient(_http, credentials), this) : null;
        var decision = await new FieldDecider(Labels, jev).DecideAsync(field, profile, jevDeadline, ct).ConfigureAwait(false);
        if (useJev) Record(decision);
        return decision;
    }

    // The user chose a value in a picker: the label is learned for the offline fallback. A "label" that is really one
    // of the copied values (OCR read the neighbouring field's text) is never written to disk.
    public void LearnChoice(FieldContext field, Candidate chosen, Profile profile)
    {
        if (field.LearnableLabel() is not { } label) return;
        var normalized = LabelNormalizer.Normalize(label);
        if (profile.Entities.SelectMany(e => e.Fields.Values).Any(value => LabelNormalizer.Normalize(value) == normalized)) return;
        new FieldDecider(Labels, null).LearnChoice(field, chosen);
    }

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

    // A route failed but another answered: hold the failed one back for a while.
    private void RouteFailed(JevEndpoint route, ServiceFailure failure)
    {
        var pause = failure switch
        {
            ServiceFailure.Unauthorized => TimeSpan.FromMinutes(30),
            ServiceFailure.PaymentRequired => TimeSpan.FromMinutes(10),
            ServiceFailure.Timeout => TimeSpan.Zero,
            _ => TimeSpan.FromMinutes(1)
        };
        if (pause == TimeSpan.Zero) return;
        lock (_routeGate) _routeBlockedUntil[route] = DateTimeOffset.Now + pause;
        Log.Event("jev_route_skipped", ("route", route.ToString()), ("reason", failure.ToString()));
    }

    private JevCredentials Credentials()
    {
        var typeSafe = CredentialStore.Get(CredentialStore.TypeSafe);
        var openRouter = CredentialStore.Get(CredentialStore.OpenRouter);
        var preference = _settings.Current.JevRoute switch
        {
            JevRoute.TypeSafe => JevRoutePreference.TypeSafe,
            JevRoute.OpenRouter => JevRoutePreference.OpenRouter,
            _ => JevRoutePreference.Auto
        };
        // Only Automatic falls through to another route; a route chosen by the user is always asked.
        if (preference == JevRoutePreference.Auto && typeSafe is not null && openRouter is not null)
        {
            var now = DateTimeOffset.Now;
            lock (_routeGate)
            {
                if (_routeBlockedUntil.TryGetValue(JevEndpoint.TypeSafe, out var until) && until > now) typeSafe = null;
                else if (_routeBlockedUntil.TryGetValue(JevEndpoint.OpenRouter, out until) && until > now) openRouter = null;
            }
        }
        return new JevCredentials(typeSafe, openRouter, preference);
    }

    private void OnKeysChanged(string target)
    {
        Interlocked.Exchange(ref _timeoutsInARow, 0);
        lock (_routeGate) _routeBlockedUntil.Clear();
        if (_guard.Reset(Credentials().HasKey)) Publish();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.JevRoute == current.JevRoute) return;
        lock (_routeGate) _routeBlockedUntil.Clear();
        if (_guard.Reset(Credentials().HasKey)) Publish();
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

    // Sees each answer before the decider does, to notice routes that failed on the way.
    private sealed class RouteWatchingChooser(JevClient inner, DecisionService owner) : IJevChooser
    {
        public async Task<JevChoice> ChooseAsync(JevQuestion question, TimeSpan deadline, CancellationToken ct)
        {
            var choice = await inner.ChooseAsync(question, deadline, ct).ConfigureAwait(false);
            foreach (var (route, failure) in choice.FailedRoutes) owner.RouteFailed(route, failure);
            return choice;
        }
    }
}

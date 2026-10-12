using System.Windows.Threading;
using SmartPaste.App.Infrastructure;

namespace SmartPaste.App.Services;

// The actions the tray, flyout, and settings can take. Keeps AppState in step with the saved settings, and
// resumes a pause when its time is up.
internal sealed class AppController : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _clock;

    public AppController(AppState state, SettingsStore settings, Dispatcher dispatcher)
    {
        State = state;
        _settings = settings;
        _dispatcher = dispatcher;
        Sync(settings.Current);
        settings.Changed += OnSettingsChanged;
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _clock.Start();
    }

    public AppState State { get; }

    // Asks whoever owns the armed block to drop it now.
    public event EventHandler? ClearRequested;

    // Asks the app to shut down.
    public event EventHandler? ExitRequested;

    public void SetEnabled(bool enabled) => _settings.Update(s => s with { Enabled = enabled, PausedUntil = null });

    public void Pause(TimeSpan duration) => _settings.Update(s => s with { PausedUntil = DateTimeOffset.Now + duration });

    public void Resume() => _settings.Update(s => s with { Enabled = true, PausedUntil = null });

    public void ClearProfile() => ClearRequested?.Invoke(this, EventArgs.Empty);

    public void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (_dispatcher.CheckAccess()) Sync(current);
        else _dispatcher.BeginInvoke(() => Sync(current));
    }

    private void Sync(AppSettings settings)
    {
        State.IsEnabled = settings.Enabled;
        State.PausedUntil = settings.IsPausedAt(DateTimeOffset.Now) ? settings.PausedUntil : null;
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (_settings.Current.PausedUntil is { } until && until <= now)
            _settings.Update(s => s with { PausedUntil = null });
        if (State.Armed is { } armed && armed.ExpiresAt <= now)
            ClearProfile();
    }

    public void Dispose()
    {
        _clock.Stop();
        _settings.Changed -= OnSettingsChanged;
    }
}

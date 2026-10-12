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

    // Raised on the UI thread when a change made through TryChange, or a pause running out, could not be written to
    // disk. The change is in effect anyway; it may just not last until the next start.
    public event EventHandler? SaveFailed;

    // These three throw IOException or UnauthorizedAccessException when the settings file can't be written. The change
    // is in effect anyway (see SettingsStore.Update). The settings window catches that and explains it in place;
    // callers with no window of their own use TryChange.
    public void SetEnabled(bool enabled) => _settings.Update(s => s with { Enabled = enabled, PausedUntil = null });

    public void Pause(TimeSpan duration) => _settings.Update(s => s with { PausedUntil = DateTimeOffset.Now + duration });

    public void Resume() => _settings.Update(s => s with { Enabled = true, PausedUntil = null });

    // Runs one of the changes above for a caller with no window to explain a failed save in: the tray menu, the
    // flyout, the on/off shortcut. A failed save is logged and reported through SaveFailed instead of thrown. source
    // names the caller in the log.
    public void TryChange(Action<AppController> change, string source) => Guard(() => change(this), source);

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

    // Runs every 15 seconds on the UI thread. Internal so tests can run it without waiting.
    internal void Tick()
    {
        var now = DateTimeOffset.Now;
        if (_settings.Current.PausedUntil is { } until && until <= now)
            Guard(() => _settings.Update(s => s with { PausedUntil = null }), "pause_end");
        if (State.Armed is { } armed && armed.ExpiresAt <= now)
            ClearProfile();
    }

    private void Guard(Action save, string source)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The store has logged the failure itself; this records what was being changed.
            Log.Error("settings_change_unsaved", ex, ("source", source));
            SaveFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _clock.Stop();
        _settings.Changed -= OnSettingsChanged;
    }
}

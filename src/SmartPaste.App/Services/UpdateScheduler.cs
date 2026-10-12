using System.Windows.Threading;
using SmartPaste.App.Infrastructure;

namespace SmartPaste.App.Services;

// Checks GitHub Releases for a newer version a few minutes after start and then every six hours, while Smart Paste
// is on and "Check for updates automatically" is set. A newer version is downloaded in the background; only once
// it is on disk does the tray show it, so "Restart to update" always works when offered. Update checks carry no
// user data.
internal sealed class UpdateScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IUpdateService _updates;
    private readonly SettingsStore _settings;
    private readonly AppState _state;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _running;

    public UpdateScheduler(IUpdateService updates, SettingsStore settings, AppState state, Dispatcher dispatcher)
    {
        _updates = updates;
        _settings = settings;
        _state = state;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) => await RunAsync();
    }

    public void Start()
    {
        if (!_updates.IsSupported) return;
        _timer.Start();
    }

    private async Task RunAsync()
    {
        _timer.Interval = CheckInterval;
        var settings = _settings.Current;
        if (_running || !settings.Enabled || !settings.CheckForUpdates || _state.Update is not null) return;
        _running = true;
        try
        {
            var result = await _updates.CheckAsync(_lifetime.Token);
            if (result is { Status: UpdateCheckStatus.Available, Update: { } update } &&
                await _updates.DownloadAsync(update, progress: null, _lifetime.Token))
                _state.Update = update;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

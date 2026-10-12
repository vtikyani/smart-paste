using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.Shell;
using SmartPaste.App.UI;
using SmartPaste.App.UI.Navigation;
using SmartPaste.Core;

namespace SmartPaste.App;

// Composition root: creates every long-lived piece of the app, wires them together, keeps them in step with the
// settings (on, off, paused, shortcuts, exclusions), and tears them down in reverse order on exit.
internal sealed class AppHost : IDisposable
{
    private readonly Application _application;
    private readonly SingleInstance _instance;
    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly LabelCache _labels;
    private readonly ClipboardWatcher _watcher;
    private readonly ClipboardPaster _paster;
    private readonly KeyboardHook _hook;
    private readonly HotkeyManager _hotkeys;
    private readonly FocusInspector _focus;
    private readonly InterceptGate _gate;
    private readonly WindowsOcr _ocr;
    private readonly ProfileStore _profiles;
    private readonly DecisionService _decisions;
    private readonly CopyPipeline _copies;
    private readonly UpdateService _updates;
    private readonly UpdateScheduler _updateChecks;
    private readonly WindowNavigator _navigator;
    private readonly UiShell _ui;
    private readonly FocusPrefetcher _prefetch;
    private readonly SmartPasteService _pastes;
    private bool? _active;
    private bool _disposed;

    public AppHost(Application application, SingleInstance instance)
    {
        _application = application;
        _instance = instance;
        _dispatcher = application.Dispatcher;

        Settings = new SettingsStore(AppPaths.SettingsFile);
        State = new AppState();
        Controller = new AppController(State, Settings, _dispatcher);
        _http = CreateHttpClient();
        _labels = new LabelCache(AppPaths.LabelCacheFile);

        _watcher = new ClipboardWatcher();
        _paster = new ClipboardPaster(_watcher);
        _hook = new KeyboardHook(_dispatcher);
        _hotkeys = new HotkeyManager();
        _focus = new FocusInspector(_dispatcher);
        _gate = new InterceptGate();
        _ocr = new WindowsOcr();

        _profiles = new ProfileStore(State, Settings, _dispatcher);
        _decisions = new DecisionService(_http, _labels, Settings, State, _dispatcher);
        _copies = new CopyPipeline(_watcher, _profiles, Settings, State, _http, _ocr, _dispatcher);
        _updates = new UpdateService(Controller.Exit);
        _updateChecks = new UpdateScheduler(_updates, Settings, State, _dispatcher);

        _navigator = new WindowNavigator(new UiServices(State, Controller, Settings, _labels, new ConnectionTester(_http),
            _updates, new PracticeEngine(_http, _decisions, Settings), _hotkeys, InterceptGate.BuiltInBlockedApps));
        _ui = new UiShell(State, Controller, Settings, _navigator, _hook);
        _prefetch = new FocusPrefetcher(_focus, _gate, _ocr, _decisions, _profiles, Settings, _ui.PasteFeedback, _dispatcher);
        _pastes = new SmartPasteService(_hook, _hotkeys, _gate, _focus, _paster, _profiles, _decisions, _prefetch,
            _ui.PasteFeedback, Settings, State, _dispatcher);
    }

    public SettingsStore Settings { get; }

    public AppState State { get; }

    public AppController Controller { get; }

    public void Start()
    {
        Log.Event("app_start", ("version", AppInfo.Version), ("kind", Program.Launch));
        if (Settings.IsFirstRun) TrySaveFirstRunSettings();
        ApplyStartAtLogin(Settings.Current.StartAtLogin);

        Controller.ClearRequested += (_, _) => _profiles.Clear();
        Controller.ExitRequested += (_, _) => _application.Shutdown();
        Settings.Changed += OnSettingsChanged;
        _hotkeys.Pressed += OnHotkey;
        _instance.OnActivationRequested(() => _dispatcher.BeginInvoke(() => _navigator.ShowSettings()));

        _ui.Start();
        Apply(Settings.Current, previous: null);
        _updateChecks.Start();

        if (!Settings.Current.OnboardingCompleted) _navigator.ShowOnboarding();
        else if (Program.Launch == LaunchKind.Manual) _navigator.ShowSettings();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (_dispatcher.CheckAccess()) Apply(current, previous);
        else _dispatcher.BeginInvoke(() => Apply(current, previous));
    }

    // Brings every piece in line with the settings. previous is null on start.
    private void Apply(AppSettings settings, AppSettings? previous)
    {
        if (_disposed) return;
        _gate.UpdateExcludedApps(settings.ExcludedApps);
        _watcher.ReadScreenshots = settings.ReadScreenshots;

        var active = settings.IsActiveAt(DateTimeOffset.Now);
        if (active != _active)
        {
            _active = active;
            if (active)
            {
                _watcher.Start();
                if (!_hook.Start()) Log.Event("hook_unavailable");
            }
            else
            {
                _hook.Stop();
                _watcher.Stop();
                _profiles.Clear();
            }
            _pastes.IsActive = active;
            Log.Event("activity", ("mode", active ? "on" : settings.Enabled ? "paused" : "off"));
        }

        // The picker shortcut only while Smart Paste is on, so it never takes a key combination while it is off;
        // the on/off shortcut always, so Smart Paste can be turned back on with it.
        var picker = active ? settings.PickerHotkey : null;
        if (previous is null || previous.PickerHotkey != settings.PickerHotkey || previous.IsActiveAt(DateTimeOffset.Now) != active)
            _hotkeys.Set(HotkeyNames.Picker, picker);
        if (previous is null || previous.ToggleHotkey != settings.ToggleHotkey)
            _hotkeys.Set(HotkeyNames.Toggle, settings.ToggleHotkey);

        if (previous is not null && previous.StartAtLogin != settings.StartAtLogin) ApplyStartAtLogin(settings.StartAtLogin);
        _prefetch.Refresh();
    }

    private void OnHotkey(string name)
    {
        if (name != HotkeyNames.Toggle) return;
        var on = Settings.Current.IsActiveAt(DateTimeOffset.Now);
        Controller.SetEnabled(!on);
        Log.Event("toggle_shortcut", ("mode", on ? "off" : "on"));
    }

    private void TrySaveFirstRunSettings()
    {
        try
        {
            Settings.SaveNow();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already logged by the store; the app runs on defaults and saves on the next change.
        }
    }

    private static void ApplyStartAtLogin(bool enabled)
    {
        try
        {
            StartupRegistration.Apply(enabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("startup_registration_error", ex);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Long-lived client: refresh connections now and then so DNS changes are picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        // Every call sets its own deadline; this is only the outer bound.
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartPaste", AppInfo.Version));
        return http;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Settings.Changed -= OnSettingsChanged;
        _hotkeys.Pressed -= OnHotkey;
        _pastes.Dispose();
        _prefetch.Dispose();
        _ui.Dispose();
        _updateChecks.Dispose();
        _copies.Dispose();
        _decisions.Dispose();
        // The hook goes before the clipboard pieces: it gives back any held keys first.
        _hook.Dispose();
        _hotkeys.Dispose();
        _focus.Dispose();
        // Puts the user's clipboard back if Smart Paste exits in the middle of a paste.
        _paster.Dispose();
        _watcher.Dispose();
        _profiles.Dispose();
        _http.Dispose();
        Controller.Dispose();
        Log.Event("app_exit");
    }
}

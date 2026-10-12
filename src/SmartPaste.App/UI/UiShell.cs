using System.Windows;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Interop;
using SmartPaste.App.UI.Popups;
using SmartPaste.App.UI.Theme;
using SmartPaste.App.UI.Tray;

namespace SmartPaste.App.UI;

// The visible app: theme, tray icon, tray flyout, and the paste feedback popups (picker and chips). Owns them all.
// Create it on the UI thread once App.xaml's resources are loaded, call Start to show the tray icon, and Dispose
// on exit. PasteFeedback is what the paste service talks to.
internal sealed class UiShell : IDisposable
{
    private readonly Application _application;
    private readonly SystemSignals _signals;
    private readonly ThemeService _theme;
    private readonly FlyoutViewModel _flyoutModel;
    private readonly FlyoutWindow _flyout;
    private readonly TrayIcon _tray;
    private readonly PasteFeedback _feedback;
    private bool _started;

    public UiShell(AppState state, AppController controller, SettingsStore settings, IWindowNavigator navigator, IKeyCapture keyCapture)
        : this(Application.Current, state, controller, settings, navigator, keyCapture, TimeProvider.System)
    {
    }

    internal UiShell(Application application, AppState state, AppController controller, SettingsStore settings,
        IWindowNavigator navigator, IKeyCapture keyCapture, TimeProvider clock)
    {
        _application = application;
        _signals = new SystemSignals(application.Dispatcher);
        _theme = new ThemeService(application, settings, _signals);
        _flyoutModel = new FlyoutViewModel(state, controller, settings, navigator, clock);
        _flyout = new FlyoutWindow(_flyoutModel);
        _feedback = new PasteFeedback(keyCapture);
        _tray = new TrayIcon(state, controller, settings, navigator, _signals, clock);
    }

    // Shows the picker and chips next to the field. Safe to call from any thread.
    public IPasteFeedback PasteFeedback => _feedback;

    public ThemeService Theme => _theme;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _theme.Start();
        _flyout.Prepare();
        _feedback.Prepare();
        // WPF makes the first window it sees the main window. Ours are hidden helpers; leave the role to the
        // first real window (settings or the welcome tour) so dialogs never get a hidden owner.
        if (_application.MainWindow is FlyoutWindow or PopupWindow) _application.MainWindow = null;
        _tray.FlyoutRequested += OnFlyoutRequested;
        _tray.Show();
    }

    // Opens the flyout beside the tray icon, for example when Smart Paste is started a second time.
    public void ShowFlyout()
    {
        if (_started) _flyout.Open(TrayAnchor());
    }

    private void OnFlyoutRequested(object? sender, EventArgs e) => _flyout.Toggle(TrayAnchor());

    // The tray icon's rectangle, or the mouse pointer when the shell cannot say (the icon is in the overflow area).
    private ScreenRect TrayAnchor()
    {
        if (_tray.Bounds() is { } icon) return icon;
        var (x, y) = WindowInterop.GetCursorPosition();
        return ScreenRect.FromPoint(x, y);
    }

    public void Dispose()
    {
        _tray.FlyoutRequested -= OnFlyoutRequested;
        _tray.Dispose();
        _feedback.Dispose();
        _flyout.Close();
        _flyoutModel.Dispose();
        _theme.Dispose();
        _signals.Dispose();
    }
}

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Brand;
using SmartPaste.App.UI.Interop;
using SmartPaste.App.UI.Text;
using SmartPaste.App.UI.Theme;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace SmartPaste.App.UI.Tray;

// The notification-area icon. Its glyph is drawn for the taskbar's exact icon size and its own light or dark
// theme, and redrawn when either changes. Left click (or Enter on the focused icon) asks for the flyout; right
// click (or Shift+F10) opens the menu. Everything it shows follows AppState.
internal sealed class TrayIcon : IDisposable
{
    private static readonly TimeSpan PulseInterval = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan TooltipInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CreateRetryInterval = TimeSpan.FromSeconds(3);

    // The shell reports one click several ways (a button-up and NIN_SELECT; Enter can repeat NIN_KEYSELECT).
    // Requests closer together than this are the same gesture.
    private static readonly TimeSpan SameGesture = TimeSpan.FromMilliseconds(500);

    private readonly AppState _state;
    private readonly AppController _controller;
    private readonly SettingsStore _settings;
    private readonly IWindowNavigator _navigator;
    private readonly SystemSignals _signals;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _pulse;
    private readonly DispatcherTimer _tooltipClock;
    private readonly DispatcherTimer _createRetry;
    private readonly Dictionary<(TrayStatus Status, int Frame), IconHandle> _icons = [];
    // Icons from an earlier size or theme. The shell may still hold one until it accepts a new icon.
    private readonly List<IconHandle> _retired = [];
    private TaskbarIcon? _taskbarIcon;
    private (int Size, TaskbarTheme Theme) _style;
    private IconHandle? _notificationIcon;
    private int _frame;
    private bool _wasArmed;
    private long _lastFlyoutRequest = long.MinValue / 2;

    public TrayIcon(AppState state, AppController controller, SettingsStore settings, IWindowNavigator navigator, SystemSignals signals, TimeProvider clock)
    {
        _state = state;
        _controller = controller;
        _settings = settings;
        _navigator = navigator;
        _signals = signals;
        _clock = clock;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _pulse = new DispatcherTimer(PulseInterval, DispatcherPriority.Background, (_, _) => NextFrame(), dispatcher) { IsEnabled = false };
        _tooltipClock = new DispatcherTimer(TooltipInterval, DispatcherPriority.Background, (_, _) => UpdateTooltip(), dispatcher) { IsEnabled = false };
        _createRetry = new DispatcherTimer(CreateRetryInterval, DispatcherPriority.Background, (_, _) => TryCreate(), dispatcher) { IsEnabled = false };
    }

    // The user clicked the icon, or pressed Enter on it from the keyboard.
    public event EventHandler? FlyoutRequested;

    public bool IsShown => _taskbarIcon is { IsCreated: true };

    public void Show()
    {
        if (_taskbarIcon is not null) return;
        _style = CurrentStyle();
        var icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(RequestFlyout),
            ContextMenu = new ContextMenu(),
            ToolTipText = Tooltip()
        };
        icon.PreviewTrayContextMenuOpen += (_, _) => BuildMenu(icon.ContextMenu);
        icon.TrayKeyboardSelect += (_, _) => RequestFlyout();
        icon.TrayKeyboardKeySelect += (_, _) => RequestFlyout();
        icon.TrayKeyboardContextMenu += (_, _) => OpenMenuFromKeyboard();
        _taskbarIcon = icon;
        _wasArmed = _state.Armed is not null;

        _state.PropertyChanged += OnStateChanged;
        _controller.SaveFailed += OnSaveFailed;
        _signals.DisplayChanged += OnSystemChanged;
        _signals.AppearanceChanged += OnSystemChanged;
        _tooltipClock.Start();
        ShowCurrentIcon();
        TryCreate();
    }

    // The icon's rectangle on screen in physical pixels, when the shell can tell (not while it sits in a closed
    // overflow area).
    public ScreenRect? Bounds()
    {
        if (_taskbarIcon is not { IsCreated: true } icon) return null;
        var id = new NativeMethods.NotifyIconIdentifier
        {
            Size = Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>(),
            Window = icon.TrayIcon.WindowHandle,
            Item = icon.Id
        };
        if (NativeMethods.Shell_NotifyIconGetRect(ref id, out var r) != 0) return null;
        var rect = ScreenRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect.IsEmpty ? null : rect;
    }

    // Adds the icon to the notification area. Right after sign-in Explorer can refuse; then this tries again
    // every few seconds, and whenever the taskbar is recreated.
    private void TryCreate()
    {
        if (_taskbarIcon is not { } icon || icon.IsCreated)
        {
            _createRetry.Stop();
            return;
        }
        try
        {
            // Efficiency mode would lower the whole process's priority, and with it the keyboard hook's.
            icon.ForceCreate(enablesEfficiencyMode: false);
            _createRetry.Stop();
            ShowCurrentIcon();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("tray_create_error", ex);
            _createRetry.Start();
        }
    }

    private void RequestFlyout()
    {
        var now = Environment.TickCount64;
        if (now - _lastFlyoutRequest < (long)SameGesture.TotalMilliseconds) return;
        _lastFlyoutRequest = now;
        FlyoutRequested?.Invoke(this, EventArgs.Empty);
    }

    // Shift+F10 or the menu key on the focused icon. A right click also reports this, after the menu is open.
    private void OpenMenuFromKeyboard()
    {
        if (_taskbarIcon is not { ContextMenu: { } menu } icon || menu.IsOpen) return;
        var (x, y) = Bounds() is { } at ? (at.Left + at.Width / 2, at.Top + at.Height / 2) : WindowInterop.GetCursorPosition();
        BuildMenu(menu);
        icon.ShowContextMenu(new System.Drawing.Point(x, y));
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        ShowCurrentIcon();
        UpdateTooltip();
        if (e.PropertyName == nameof(AppState.Armed)) AnnounceArmed();
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        if (_taskbarIcon is { IsCreated: false }) TryCreate();
        var style = CurrentStyle();
        if (style == _style) return;
        _retired.AddRange(_icons.Values);
        _icons.Clear();
        _style = style;
        ShowCurrentIcon();
    }

    private static (int Size, TaskbarTheme Theme) CurrentStyle() => (Taskbar.IconSize(Taskbar.Dpi()), Taskbar.Theme());

    private void ShowCurrentIcon()
    {
        if (_taskbarIcon is null) return;
        var status = _state.Status;
        var frames = TrayIconRenderer.FrameCount(status);
        // With Windows animations turned off the badge holds still, fully lit.
        var animate = frames > 1 && SystemParameters.ClientAreaAnimation;
        if (animate && !_pulse.IsEnabled)
        {
            _frame = 0;
            _pulse.Start();
        }
        else if (!animate)
        {
            _pulse.Stop();
        }
        var key = (status, frames == 1 ? 0 : animate ? _frame % frames : frames / 2);
        if (!_icons.TryGetValue(key, out var handle))
        {
            handle = IconHandle.FromBitmap(TrayIconRenderer.Render(status, _style.Theme, _style.Size, key.Item2));
            _icons[key] = handle;
        }
        // Retired icons may be freed only once the shell holds the new one.
        if (!_taskbarIcon.TrayIcon.UpdateIcon(handle.DangerousGetHandle())) return;
        foreach (var old in _retired) old.Dispose();
        _retired.Clear();
    }

    private void NextFrame()
    {
        _frame = (_frame + 1) % TrayIconRenderer.PulseFrames;
        ShowCurrentIcon();
    }

    private void UpdateTooltip()
    {
        if (_taskbarIcon is null) return;
        var text = Tooltip();
        if (_taskbarIcon.ToolTipText != text) _taskbarIcon.ToolTipText = text;
    }

    private string Tooltip() => StatusText.Tooltip(_state, _clock.GetLocalNow(), CultureInfo.CurrentCulture);

    // One notification per copied block, when the user asked for them. A richer parse of the same copy replaces
    // the armed block without clearing it first, so only a change from nothing armed counts as a new block. The
    // notification is optional: if Windows refuses it, that is logged and nothing else is affected.
    private void AnnounceArmed()
    {
        var armed = _state.Armed;
        var isNew = armed is not null && !_wasArmed;
        _wasArmed = armed is not null;
        if (!isNew || _taskbarIcon is not { IsCreated: true } icon || !_settings.Current.NotifyWhenArmed) return;
        if (_state.Status is TrayStatus.Off or TrayStatus.Paused) return;

        try
        {
            // The shell wants the large icon at the size it was at sign-in, whatever the current display scale.
            var size = Taskbar.NotificationIconSize();
            if (_notificationIcon is null || _notificationIcon.Size != size)
            {
                _notificationIcon?.Dispose();
                _notificationIcon = IconHandle.FromBitmap(RenderLogo(size));
            }
            icon.ShowNotification(
                title: StatusText.FieldCount(armed!.FieldCount) + " ready to paste",
                message: "Click into a form field and press Ctrl+V.",
                icon: NotificationIcon.None,
                customIconHandle: _notificationIcon.DangerousGetHandle(),
                largeIcon: true,
                sound: false,
                respectQuietTime: true);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("tray_notification_error", ex);
        }
    }

    // A change from the tray menu, the flyout, the on/off shortcut, or a pause running out couldn't be written to
    // disk. It works anyway; this says it may not last. There is no window to say it in, so it is a notification.
    private void OnSaveFailed(object? sender, EventArgs e)
    {
        if (_taskbarIcon is not { IsCreated: true } icon) return;
        try
        {
            icon.ShowNotification(
                title: "Smart Paste couldn't save its settings",
                message: "Your change works for now, but Smart Paste may forget it when it closes. Make sure your disk isn't full.",
                icon: NotificationIcon.Warning,
                sound: false);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("tray_notification_error", ex);
        }
    }

    private static BitmapSource RenderLogo(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(BrandArt.CreateLogo(Math.Max(16, size)));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void BuildMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var now = _clock.GetLocalNow();
        if (_state.IsEnabled)
        {
            menu.Items.Add(Item("Turn off", SymbolRegular.Power24, () => Change(c => c.SetEnabled(false))));
            if (_state.PausedUntil is { } until && until > now)
            {
                menu.Items.Add(Item("Resume", SymbolRegular.Play24, () => Change(c => c.Resume())));
            }
            else
            {
                var pause = Item("Pause", SymbolRegular.Pause24, null);
                foreach (var option in PauseOption.All)
                    pause.Items.Add(Item(option.Label, null, () => Change(c => c.Pause(option.Length(_clock.GetLocalNow())))));
                menu.Items.Add(pause);
            }
        }
        else
        {
            menu.Items.Add(Item("Turn on", SymbolRegular.Power24, () => Change(c => c.SetEnabled(true))));
        }
        if (_state.Armed is not null)
            menu.Items.Add(Item("Clear copied block", SymbolRegular.Broom24, _controller.ClearProfile));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", SymbolRegular.Settings24, () => _navigator.ShowSettings()));
        menu.Items.Add(Item("Exit", SymbolRegular.ArrowExit20, _controller.Exit));
    }

    private void Change(Action<AppController> change) => _controller.TryChange(change, "tray");

    private static MenuItem Item(string header, SymbolRegular? symbol, Action? action)
    {
        var item = new MenuItem { Header = header };
        if (symbol is { } s) item.Icon = new SymbolIcon { Symbol = s, FontSize = 16 };
        if (action is not null) item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _pulse.Stop();
        _tooltipClock.Stop();
        _createRetry.Stop();
        _state.PropertyChanged -= OnStateChanged;
        _controller.SaveFailed -= OnSaveFailed;
        _signals.DisplayChanged -= OnSystemChanged;
        _signals.AppearanceChanged -= OnSystemChanged;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
        foreach (var handle in _icons.Values.Concat(_retired)) handle.Dispose();
        _icons.Clear();
        _retired.Clear();
        _notificationIcon?.Dispose();
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct NotifyIconIdentifier
        {
            public int Size;
            public IntPtr Window;
            public uint Id;
            public Guid Item;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("shell32.dll")]
        public static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);
    }
}

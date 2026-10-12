using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Brand;
using SmartPaste.App.UI.Text;
using SmartPaste.App.UI.Theme;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace SmartPaste.App.UI.Tray;

// The notification-area icon. Its glyph is drawn for the taskbar's exact icon size and its own light or dark
// theme, and redrawn when either changes. Left click (or Enter on the focused icon) asks for the flyout; right
// click opens the menu. Everything it shows follows AppState.
internal sealed class TrayIcon : IDisposable
{
    private static readonly TimeSpan PulseInterval = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan TooltipInterval = TimeSpan.FromSeconds(15);

    private readonly AppState _state;
    private readonly AppController _controller;
    private readonly SettingsStore _settings;
    private readonly IWindowNavigator _navigator;
    private readonly SystemSignals _signals;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _pulse;
    private readonly DispatcherTimer _tooltipClock;
    private readonly Dictionary<(TrayStatus Status, int Frame), IconHandle> _icons = [];
    private TaskbarIcon? _taskbarIcon;
    private (int Size, TaskbarTheme Theme) _style;
    private IconHandle? _notificationIcon;
    private int _frame;
    private bool _wasArmed;

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
        _taskbarIcon = icon;
        _wasArmed = _state.Armed is not null;
        ShowCurrentIcon();
        // Efficiency mode would lower the whole process's priority, and with it the keyboard hook's.
        icon.ForceCreate(enablesEfficiencyMode: false);
        ShowCurrentIcon();

        _state.PropertyChanged += OnStateChanged;
        _signals.DisplayChanged += OnSystemChanged;
        _signals.AppearanceChanged += OnSystemChanged;
        _tooltipClock.Start();
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

    private void RequestFlyout() => FlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        ShowCurrentIcon();
        UpdateTooltip();
        if (e.PropertyName == nameof(AppState.Armed)) AnnounceArmed();
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        var style = CurrentStyle();
        if (style == _style) return;
        // Draw the new size or theme first, then free the old handles the shell no longer shows.
        var old = _icons.Values.ToList();
        _icons.Clear();
        _style = style;
        ShowCurrentIcon();
        foreach (var handle in old) handle.Dispose();
        _notificationIcon?.Dispose();
        _notificationIcon = null;
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
        _taskbarIcon.TrayIcon.UpdateIcon(handle.DangerousGetHandle());
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
    // the armed block without clearing it first, so only a change from nothing armed counts as a new block.
    private void AnnounceArmed()
    {
        var armed = _state.Armed;
        var isNew = armed is not null && !_wasArmed;
        _wasArmed = armed is not null;
        if (!isNew || _taskbarIcon is null || !_settings.Current.NotifyWhenArmed) return;
        if (_state.Status is TrayStatus.Off or TrayStatus.Paused) return;

        _notificationIcon ??= IconHandle.FromBitmap(RenderLogo(Taskbar.LargeIconSize(Taskbar.Dpi())));
        _taskbarIcon.ShowNotification(
            title: StatusText.FieldCount(armed!.FieldCount) + " ready to paste",
            message: "Click into a form field and press Ctrl+V.",
            icon: NotificationIcon.None,
            customIconHandle: _notificationIcon.DangerousGetHandle(),
            largeIcon: true,
            sound: false,
            respectQuietTime: true);
    }

    private static System.Windows.Media.Imaging.BitmapSource RenderLogo(int size)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(BrandArt.CreateLogo(size));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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
            menu.Items.Add(Item("Turn off", SymbolRegular.Power24, () => _controller.SetEnabled(false)));
            if (_state.PausedUntil is { } until && until > now)
            {
                menu.Items.Add(Item("Resume", SymbolRegular.Play24, _controller.Resume));
            }
            else
            {
                var pause = Item("Pause", SymbolRegular.Pause24, null);
                foreach (var option in PauseOption.All)
                    pause.Items.Add(Item(option.Label, null, () => _controller.Pause(option.Length(_clock.GetLocalNow()))));
                menu.Items.Add(pause);
            }
        }
        else
        {
            menu.Items.Add(Item("Turn on", SymbolRegular.Power24, () => _controller.SetEnabled(true)));
        }
        if (_state.Armed is not null)
            menu.Items.Add(Item("Clear copied block", SymbolRegular.Broom24, _controller.ClearProfile));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", SymbolRegular.Settings24, () => _navigator.ShowSettings()));
        menu.Items.Add(Item("Exit", SymbolRegular.ArrowExit20, _controller.Exit));
    }

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
        _state.PropertyChanged -= OnStateChanged;
        _signals.DisplayChanged -= OnSystemChanged;
        _signals.AppearanceChanged -= OnSystemChanged;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
        foreach (var handle in _icons.Values) handle.Dispose();
        _icons.Clear();
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

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Interop;

namespace SmartPaste.App.UI.Tray;

// The panel that opens from the tray icon. It sits beside the taskbar like Windows 11's own flyouts, uses an
// acrylic backdrop, and closes when it loses focus or on Escape. Created once at startup and reused.
internal partial class FlyoutWindow
{
    // Gap to the taskbar and the screen edges, as in Windows 11 flyouts.
    private const double EdgeMargin = 12;

    // A click on the tray icon first deactivates (and hides) an open flyout; that same click must not reopen it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private readonly FlyoutViewModel _viewModel;
    private ScreenRect _anchor;
    private TaskbarPosition? _taskbar;
    private long _hiddenAt;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.CloseRequested += (_, _) => Dismiss(byDeactivation: false);
        SourceInitialized += (_, _) => WindowInterop.AddExtendedStyle(Handle, WindowInterop.ExToolWindow);
        Deactivated += (_, _) => Dismiss(byDeactivation: true);
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
    }

    private IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    // Creates the native window ahead of time so the first open is instant.
    public void Prepare() => _ = Handle;

    // Opens the flyout beside the tray icon, or closes it when it is open.
    public void Toggle(ScreenRect anchor)
    {
        if (IsVisible)
        {
            Dismiss(byDeactivation: false);
            return;
        }
        if (Environment.TickCount64 - _hiddenAt < (long)ReopenGuard.TotalMilliseconds) return;
        Open(anchor);
    }

    public void Open(ScreenRect anchor)
    {
        _anchor = anchor;
        _taskbar = Taskbar.Position();
        _viewModel.Opened();

        var display = Displays.FromRect(anchor);
        var margin = display.ToPixels(EdgeMargin);
        MaxHeight = FlyoutPlacement.MaxHeight(display.WorkArea, _taskbar, margin) / display.Scale;

        // Place it before it appears, from the content's measured height, so the backdrop never jumps.
        Root.Measure(new Size(Width, MaxHeight));
        var height = Math.Min(Root.DesiredSize.Height, MaxHeight);
        var (x, y) = FlyoutPlacement.Place(display.ToPixels(Width), display.ToPixels(height), anchor, display.WorkArea, _taskbar, margin);
        WindowInterop.Move(Handle, x, y);

        Root.Opacity = 0;
        Show();
        Activate();
        WindowInterop.BringToForeground(Handle);
        Reposition();
        PlayEnter();
    }

    // Keeps the flyout against the taskbar when its height changes (a block arrives or clears while it is open)
    // or when it lands on a monitor with another display scale.
    private void Reposition()
    {
        if (!IsVisible) return;
        var display = Displays.FromRect(_anchor);
        var bounds = WindowInterop.GetBounds(Handle);
        if (bounds.IsEmpty) return;
        var (x, y) = FlyoutPlacement.Place(bounds.Width, bounds.Height, _anchor, display.WorkArea, _taskbar, display.ToPixels(EdgeMargin));
        if (x != bounds.Left || y != bounds.Top) WindowInterop.Move(Handle, x, y);
    }

    // Hides the flyout; it is reused, never closed.
    public void Dismiss(bool byDeactivation = false)
    {
        if (!IsVisible) return;
        if (byDeactivation) _hiddenAt = Environment.TickCount64;
        Root.BeginAnimation(OpacityProperty, null);
        EnterOffset.BeginAnimation(TranslateTransform.XProperty, null);
        EnterOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _viewModel.Closed();
        Hide();
    }

    // Content fades in and settles 8 px toward the taskbar side, decelerating. With Windows animations off it
    // only fades, quickly.
    private void PlayEnter()
    {
        var motion = SystemParameters.ClientAreaAnimation;
        var duration = TimeSpan.FromMilliseconds(motion ? 180 : 90);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (!motion) return;

        var offset = (double)FindResource("Brand.EnterOffset");
        var (dx, dy) = (_taskbar?.Edge ?? TaskbarEdge.Bottom) switch
        {
            TaskbarEdge.Top => (0.0, -offset),
            TaskbarEdge.Left => (-offset, 0.0),
            TaskbarEdge.Right => (offset, 0.0),
            _ => (0.0, offset)
        };
        EnterOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, duration) { EasingFunction = ease });
        EnterOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, duration) { EasingFunction = ease });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Dismiss();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (PauseButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = PauseButton;
        menu.IsOpen = true;
    }
}

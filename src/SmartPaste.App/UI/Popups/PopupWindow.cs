using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Interop;

namespace SmartPaste.App.UI.Popups;

// A floating window that never takes focus from the app the user is typing in: it does not activate when shown
// or clicked, has no taskbar or Alt+Tab entry, and stays on top. Click-through popups also let the mouse pass to
// the app below. It is created once and reused, and positioned in physical pixels on the anchor's monitor.
//
// The window is larger than what it shows: a transparent margin around the visible surface holds its shadow.
// Placement works on the surface, so the shadow never pushes it away from its anchor.
internal class PopupWindow : Window
{
    private readonly bool _clickThrough;
    private int _fadeVersion;

    // Parameterless for XAML; subclasses that let clicks through pass true.
    public PopupWindow() : this(clickThrough: false)
    {
    }

    protected PopupWindow(bool clickThrough)
    {
        _clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        UseLayoutRounding = true;
        SourceInitialized += OnSourceInitialized;
    }

    protected IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    // The root element: the shadow margin is its Margin, the surface is everything inside it.
    protected FrameworkElement Surface => (FrameworkElement)Content;

    // True from the moment the popup is asked to show until it is asked to hide (fades included).
    public bool IsShowing { get; private set; }

    // Creates the native window and lays it out once, so the first show is instant.
    public void Prepare()
    {
        _ = Handle;
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    // The visible surface's bounds on screen, in physical pixels.
    public ScreenRect SurfaceBounds()
    {
        var window = WindowInterop.GetBounds(Handle);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var m = Surface.Margin;
        return ScreenRect.FromLTRB(
            window.Left + (int)Math.Round(m.Left * scale),
            window.Top + (int)Math.Round(m.Top * scale),
            window.Right - (int)Math.Round(m.Right * scale),
            window.Bottom - (int)Math.Round(m.Bottom * scale));
    }

    // Shows the popup (or moves it, if it is showing) with its surface where place puts it. place gets the
    // surface's size in physical pixels and the anchor's monitor, and returns the surface's top-left.
    protected void ShowAt(ScreenRect anchor, Func<int, int, Display, (int X, int Y)> place)
    {
        var display = Displays.FromRect(anchor);
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Surface.DesiredSize;
        var m = Surface.Margin;
        var width = display.ToPixels(size.Width - m.Left - m.Right);
        var height = display.ToPixels(size.Height - m.Top - m.Bottom);
        var (x, y) = place(width, height, display);
        WindowInterop.MoveTopmost(Handle, x - display.ToPixels(m.Left), y - display.ToPixels(m.Top));
        IsShowing = true;
        if (!IsVisible) Show();
        // Raise above other topmost windows that appeared since, still without activating.
        WindowInterop.MoveTopmost(Handle, x - display.ToPixels(m.Left), y - display.ToPixels(m.Top));
    }

    // Fades the surface in, optionally drifting from a small offset. Instant when Windows animations are off.
    protected void FadeIn(TimeSpan duration, double fromY = 0)
    {
        _fadeVersion++;
        var motion = SystemParameters.ClientAreaAnimation;
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(motion ? 0 : 1, 1, motion ? duration : TimeSpan.Zero) { EasingFunction = ease });
        if (Surface.RenderTransform is TranslateTransform shift)
            shift.BeginAnimation(TranslateTransform.YProperty, motion && fromY != 0 ? new DoubleAnimation(fromY, 0, duration) { EasingFunction = ease } : null);
    }

    // Fades the surface out and hides the window, unless it is shown again first.
    protected void FadeOutAndHide(TimeSpan duration)
    {
        IsShowing = false;
        if (!IsVisible) return;
        if (!SystemParameters.ClientAreaAnimation || duration <= TimeSpan.Zero)
        {
            HideNow();
            return;
        }
        var version = ++_fadeVersion;
        var fade = new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (version == _fadeVersion) HideNow();
        };
        Surface.BeginAnimation(OpacityProperty, fade);
    }

    // Popups never take focus, so screen readers would not notice them; this tells them what appeared.
    protected void Announce(string text)
    {
        var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent,
            text, "SmartPaste.Popup");
    }

    protected void HideNow()
    {
        _fadeVersion++;
        IsShowing = false;
        Surface.BeginAnimation(OpacityProperty, null);
        Surface.Opacity = 0;
        Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var style = WindowInterop.ExNoActivate | WindowInterop.ExToolWindow | WindowInterop.ExTopmost;
        if (_clickThrough) style |= WindowInterop.ExTransparent | WindowInterop.ExLayered;
        WindowInterop.AddExtendedStyle(Handle, style);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A click must never activate the popup: the target app keeps focus and its caret.
        if (message != WindowInterop.WmMouseActivate) return IntPtr.Zero;
        handled = true;
        return new IntPtr(WindowInterop.MaNoActivate);
    }
}

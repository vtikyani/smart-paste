using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SmartPaste.App.UI.Theme;

// Listens for Windows-wide changes the UI follows: light/dark theme, accent color, high contrast, display scale,
// monitors, the taskbar moving, and Explorer restarting. It owns a hidden top-level window because broadcast
// messages never reach message-only windows. Windows sends these in bursts, so each kind is coalesced into one
// event raised on the UI thread.
internal sealed class SystemSignals : IDisposable
{
    private const int WmSysColorChange = 0x0015;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmColorizationColorChanged = 0x0320;
    private const int SpiSetWorkArea = 0x002F;
    private const int SpiSetHighContrast = 0x0043;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private readonly HwndSource _window;
    private readonly DispatcherTimer _debounce;
    private readonly int _taskbarCreated;
    private bool _appearancePending;
    private bool _displayPending;

    public SystemSignals(Dispatcher dispatcher)
    {
        _taskbarCreated = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _window = new HwndSource(new HwndSourceParameters("SmartPaste.SystemSignals")
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero
        });
        _window.AddHook(WndProc);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => Flush(), dispatcher);
    }

    // The theme, accent color, or high contrast setting changed.
    public event EventHandler? AppearanceChanged;

    // Display scale, resolution, monitors, the taskbar, or Explorer changed.
    public event EventHandler? DisplayChanged;

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmSettingChange:
                var area = lParam != IntPtr.Zero ? Marshal.PtrToStringUni(lParam) : null;
                if (area == "ImmersiveColorSet" || wParam.ToInt64() == SpiSetHighContrast) Signal(appearance: true);
                if (wParam.ToInt64() == SpiSetWorkArea) Signal(display: true);
                break;
            case WmSysColorChange:
            case WmThemeChanged:
            case WmDwmColorizationColorChanged:
                Signal(appearance: true);
                break;
            case WmDisplayChange:
            case WmDpiChanged:
                Signal(display: true);
                break;
            default:
                if (message == _taskbarCreated) Signal(display: true);
                break;
        }
        return IntPtr.Zero;
    }

    private void Signal(bool appearance = false, bool display = false)
    {
        _appearancePending |= appearance;
        _displayPending |= display;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        _debounce.Stop();
        var appearance = _appearancePending;
        var display = _displayPending;
        _appearancePending = _displayPending = false;
        if (appearance) AppearanceChanged?.Invoke(this, EventArgs.Empty);
        if (display) DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _debounce.Stop();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string name);
    }
}

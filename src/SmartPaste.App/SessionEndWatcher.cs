using System.Windows.Interop;

namespace SmartPaste.App;

// A hidden top-level window that hears when Windows ends the session (restart, shut down, sign out, or an installer
// closing apps). Only top-level windows get these messages.
// - WM_QUERYENDSESSION asks whether the session may end. Smart Paste never objects, and changes nothing yet: the user
//   or another app can still cancel, and then Smart Paste must still be running.
// - WM_ENDSESSION with wParam TRUE says it is ending for real. Windows may end the process as soon as the message
//   returns, so the callback cleans up right there, synchronously. With FALSE the end was canceled: nothing to do.
internal sealed class SessionEndWatcher : IDisposable
{
    internal const int WmQueryEndSession = 0x0011;
    internal const int WmEndSession = 0x0016;

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private readonly Action _sessionEnding;
    private readonly HwndSource _window;

    public SessionEndWatcher(Action sessionEnding)
    {
        _sessionEnding = sessionEnding;
        _window = new HwndSource(new HwndSourceParameters("SmartPaste.Session")
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero
        });
        _window.AddHook(WndProc);
    }

    // The window's handle, for tests.
    internal IntPtr Handle => _window.Handle;

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmQueryEndSession:
                handled = true;
                return new IntPtr(1);
            case WmEndSession when wParam != IntPtr.Zero:
                handled = true;
                _sessionEnding();
                return IntPtr.Zero;
            default:
                return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}

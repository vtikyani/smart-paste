using System.Runtime.InteropServices;
using SmartPaste.App.Native;

namespace SmartPaste.App;
// The hook lives on its own thread with its own message loop. The UI thread does clipboard, GDI+, and
// OCR work that can take hundreds of ms; a hook sharing that thread would stall every keystroke system-wide
// and Windows would silently drop the hook past LowLevelHooksTimeout.
// Only Ctrl+V (no Shift/Alt/Win) is ever swallowed. Ctrl+Shift+V always passes through untouched, so the
// target app pastes the original clipboard with its own (often plain-text) paste.
internal sealed class KeyboardHook : IDisposable
{
    public const nuint InjectedMarker=0x5350_5354;
    private readonly Win32.HookProc _callback;
    private readonly InterceptGate _gate;
    private readonly Control _dispatcher;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    internal event Action<IntPtr>? SmartPasteRequested;
    internal KeyboardHook(InterceptGate gate,Control dispatcher)
    {
        _gate=gate; _dispatcher=dispatcher; _callback=OnKeyboard;
    }
    internal void Start()
    {
        if(_thread is not null) return;
        var ready=new ManualResetEventSlim(); // not disposed: a late Set() after a timed-out Wait must not throw
        var thread=new Thread(() =>
        {
            try
            {
                _threadId=Win32.GetCurrentThreadId();
                Win32.PeekMessage(out _,IntPtr.Zero,0,0,Win32.PmNoRemove); // create the queue so Stop's WM_QUIT cannot be lost
                _hook=Win32.SetWindowsHookEx(Win32.WhKeyboardLl,_callback,Win32.GetModuleHandle(null),0);
                if(_hook==IntPtr.Zero) { Log.Event("hook_install_error"); return; }
                ready.Set();
                while(Win32.GetMessage(out _,IntPtr.Zero,0,0)>0) { }
            }
            catch { Log.Event("hook_thread_error"); }
            finally
            {
                if(_hook!=IntPtr.Zero) { Win32.UnhookWindowsHookEx(_hook); _hook=IntPtr.Zero; }
                ready.Set();
            }
        }) { IsBackground=true,Name="SmartPaste keyboard hook" };
        thread.Start();
        ready.Wait(2000);
        if(_hook==IntPtr.Zero) { thread.Join(500); return; } // install failed: stay stopped so a later Start can retry
        _thread=thread;
    }
    internal void Stop()
    {
        if(_thread is null) return;
        for(int i=0;i<10 && !Win32.PostThreadMessage(_threadId,Win32.WmQuit,IntPtr.Zero,IntPtr.Zero);i++) Thread.Sleep(20);
        if(_thread.Join(2000)) _thread=null;
        else Log.Event("hook_stop_timeout");
    }
    private IntPtr OnKeyboard(int code,IntPtr wParam,IntPtr lParam)
    {
        try
        {
            if(code<0 || (int)wParam!=Win32.WmKeyDown && (int)wParam!=Win32.WmSysKeyDown) return Win32.CallNextHookEx(_hook,code,wParam,lParam);
            var key=Marshal.PtrToStructure<Win32.KbdLlHookStruct>(lParam);
            if(key.ExtraInfo==InjectedMarker || key.VkCode!=Win32.VkV) return Win32.CallNextHookEx(_hook,code,wParam,lParam);
            static bool Down(int vk) => (Win32.GetAsyncKeyState(vk)&0x8000)!=0;
            if(!Down(Win32.VkControl) || Down(Win32.VkShift) || Down(Win32.VkMenu) || Down(Win32.VkLWin) || Down(Win32.VkRWin))
                return Win32.CallNextHookEx(_hook,code,wParam,lParam);
            var hwnd=Win32.GetForegroundWindow();
            if(!_gate.ShouldIntercept(hwnd)) return Win32.CallNextHookEx(_hook,code,wParam,lParam);
            _dispatcher.BeginInvoke((Action)(() => SmartPasteRequested?.Invoke(hwnd)));
            return new IntPtr(1);
        }
        catch { return Win32.CallNextHookEx(_hook,code,wParam,lParam); }
    }
    public void Dispose() => Stop();
}

using System.Windows.Interop;

namespace SmartPaste.App.Shell;

// An invisible message-only window on the thread that creates it (the UI thread), for Windows APIs that talk to a
// window: hotkeys, clipboard notifications, and delayed clipboard rendering. Message-only windows receive no
// broadcasts and never show up anywhere.
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow(string name, HwndSourceHook hook)
    {
        var parameters = new HwndSourceParameters(name)
        {
            ParentWindow = NativeMethods.MessageOnlyParent,
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(hook);
    }

    public IntPtr Handle => _source.Handle;

    public void Dispose() => _source.Dispose();
}

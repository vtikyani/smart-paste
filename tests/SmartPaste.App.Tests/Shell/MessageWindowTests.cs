using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

// The hotkey manager, clipboard watcher, and paster all receive their messages through MessageWindow.
public sealed class MessageWindowTests
{
    private const int TestMessage = 0x8001;

    [Fact]
    public void IsAMessageOnlyWindowThatReceivesItsMessages()
    {
        RunOnSta(() =>
        {
            var received = new List<(int Message, IntPtr WParam)>();
            IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == TestMessage)
                {
                    received.Add((message, wParam));
                    handled = true;
                }
                return IntPtr.Zero;
            }
            using var window = new MessageWindow("SmartPaste.MessageWindowTest", Hook);

            Assert.NotEqual(IntPtr.Zero, window.Handle);
            // Only message-only windows are found under HWND_MESSAGE.
            Assert.Equal(window.Handle, FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "SmartPaste.MessageWindowTest"));
            Assert.False(IsWindowVisible(window.Handle));

            SendMessage(window.Handle, TestMessage, new IntPtr(42), IntPtr.Zero);
            Assert.Equal(new[] { (TestMessage, new IntPtr(42)) }, received);
        });
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

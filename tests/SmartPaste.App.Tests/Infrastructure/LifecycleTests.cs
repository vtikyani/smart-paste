using System.Runtime.InteropServices;
using System.Windows.Threading;
using SmartPaste.App;
using SmartPaste.App.Infrastructure;

// How Smart Paste ends: Windows ending the session, a copy started with --exit, and errors before startup finished.
public sealed class LifecycleTests
{
    private static readonly IntPtr EndSessionLogoff = new(0x80000000L);

    [Fact]
    public void ACanceledSignOutChangesNothingAndARealOneEndsTheApp() => WpfHost.Run(() =>
    {
        var ended = 0;
        using var watcher = new SessionEndWatcher(() => ended++);

        // Windows asks first. Smart Paste never objects, and must not stop yet: the user can still cancel.
        Assert.Equal(new IntPtr(1), NativeMethods.SendMessage(watcher.Handle, SessionEndWatcher.WmQueryEndSession, IntPtr.Zero, EndSessionLogoff));
        // Canceled.
        NativeMethods.SendMessage(watcher.Handle, SessionEndWatcher.WmEndSession, IntPtr.Zero, EndSessionLogoff);
        Assert.Equal(0, ended);

        // Ending for real: the cleanup runs before the message returns.
        NativeMethods.SendMessage(watcher.Handle, SessionEndWatcher.WmEndSession, new IntPtr(1), EndSessionLogoff);
        Assert.Equal(1, ended);
    });

    [Fact]
    public void ExitArgumentAsksTheRunningCopyToExitAndWaitsUntilItHas()
    {
        var name = TestName();
        using var running = new RunningCopy(name);
        running.ExitWhenAsked();

        using var second = SingleInstance.Acquire(name);
        Assert.False(second.IsPrimary);
        Assert.True(second.RequestExit(TimeSpan.FromSeconds(10)));
        Assert.True(running.WaitForExit(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ExitArgumentGivesUpOnACopyThatDoesNotExit()
    {
        var name = TestName();
        using var running = new RunningCopy(name);

        using var second = SingleInstance.Acquire(name);
        Assert.False(second.RequestExit(TimeSpan.FromMilliseconds(300)));
        Assert.False(running.WaitForExit(TimeSpan.Zero));
    }

    [Fact]
    public void AnErrorBeforeStartupFinishedEndsTheRunAndOneAfterIsSurvived()
    {
        var started = false;
        var startupErrors = new List<Exception>();
        Exception? escaped = null;
        var thread = new Thread(() =>
        {
            // A dispatcher of its own: the shared UI thread's error handling must stay as it is.
            var dispatcher = Dispatcher.CurrentDispatcher;
            CrashReporter.Attach(dispatcher, () => started, startupErrors.Add);
            dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("while starting")));
            dispatcher.BeginInvoke(new Action(() => started = true));
            dispatcher.BeginInvoke(new Action(() => throw new FormatException("while running")));
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            try
            {
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

        Assert.IsType<InvalidOperationException>(Assert.Single(startupErrors));
        Assert.Null(escaped);
    }

    private static string TestName() => @"Local\SmartPaste.Test." + Guid.NewGuid().ToString("N");

    // A running Smart Paste as far as the single-instance lock goes: its own thread holds the lock (a mutex belongs to
    // the thread that took it) until it is asked to exit, or until the test ends.
    private sealed class RunningCopy : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _stop = new();
        private SingleInstance? _instance;

        public RunningCopy(string name)
        {
            _thread = new Thread(() =>
            {
                using var instance = SingleInstance.Acquire(name);
                _instance = instance;
                _ready.Set();
                _stop.Wait();
            }) { IsBackground = true };
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_instance!.IsPrimary);
        }

        public bool WaitForExit(TimeSpan timeout) => _thread.Join(timeout);

        // As the app does: a request from --exit makes it exit, which lets go of the lock.
        public void ExitWhenAsked() => _instance!.OnExitRequested(_stop.Set);

        public void Dispose()
        {
            _stop.Set();
            _thread.Join(TimeSpan.FromSeconds(10));
            _ready.Dispose();
            _stop.Dispose();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}

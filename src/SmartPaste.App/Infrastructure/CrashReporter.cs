using System.Windows.Threading;

namespace SmartPaste.App.Infrastructure;

// Last-chance error handling. A tray utility must not vanish because one window or one background task
// failed, so once it is running, UI-thread errors are logged and survived; only truly fatal errors end the process.
internal static class CrashReporter
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Crash(ex, "unhandled");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash(e.Exception, "unobserved_task");
            e.SetObserved();
        };
    }

    // Hooks the WPF dispatcher once it exists. Until isRunning says startup has finished, an error means Smart Paste
    // can't run: it goes to onStartupFailure (which logs it and exits) instead of being survived, so no process is
    // left behind with no tray icon, holding the single-instance lock.
    public static void Attach(Dispatcher dispatcher, Func<bool> isRunning, Action<Exception> onStartupFailure)
    {
        dispatcher.UnhandledException += (_, e) =>
        {
            if (!isRunning())
            {
                e.Handled = true;
                onStartupFailure(e.Exception);
                return;
            }
            Log.Crash(e.Exception, "ui_thread");
            e.Handled = !IsFatal(e.Exception);
        };
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or StackOverflowException or AccessViolationException or InvalidProgramException;
}

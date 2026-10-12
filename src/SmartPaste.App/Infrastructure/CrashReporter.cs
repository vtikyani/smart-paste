using System.Windows.Threading;

namespace SmartPaste.App.Infrastructure;

// Last-chance error handling. A tray utility must not vanish because one window or one background task
// failed, so UI-thread errors are logged and survived; only truly fatal errors end the process.
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

    // Hooks the WPF dispatcher once it exists.
    public static void Attach(Dispatcher dispatcher)
    {
        dispatcher.UnhandledException += (_, e) =>
        {
            Log.Crash(e.Exception, "ui_thread");
            e.Handled = !IsFatal(e.Exception);
        };
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or StackOverflowException or AccessViolationException or InvalidProgramException;
}

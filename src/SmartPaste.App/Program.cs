namespace SmartPaste.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SmartPaste.v1", out var first);
        if (!first) return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, _) => Log.Event("ui_error");
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Log.Event("unhandled_error");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Event("task_error"); e.SetObserved(); };
        try { Application.Run(new TrayContext()); }
        catch { Log.Event("app_error"); }
    }
}

using SmartPaste.App.Infrastructure;
using Velopack;

namespace SmartPaste.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Must run first. The installer starts this exe with special arguments for install, update, and
        // uninstall steps; Velopack handles those here and exits the process when one is being handled.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
            .Run();

        Log.Initialize(AppPaths.LogDirectory);
        CrashReporter.Install();

        using var instance = SingleInstance.Acquire();
        if (!instance.IsPrimary)
        {
            instance.SignalPrimary();
            return 0;
        }

        var app = new App(instance);
        app.InitializeComponent();
        return app.Run();
    }
}

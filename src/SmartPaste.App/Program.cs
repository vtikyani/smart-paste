using SmartPaste.App.Infrastructure;
using Velopack;

namespace SmartPaste.App;

internal static class Program
{
    // How this run started, so a manual start can show a window while a start at sign-in stays quietly in the tray.
    public static LaunchKind Launch { get; private set; } = LaunchKind.Manual;

    [STAThread]
    private static int Main(string[] args)
    {
        // Must run first. The installer starts this exe with special arguments for install, update, and
        // uninstall steps; Velopack handles those here and exits the process when one is being handled.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
            .OnRestarted(_ => Launch = LaunchKind.AfterUpdate)
            .Run();

        if (args.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase)) Launch = LaunchKind.AtSignIn;

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

internal enum LaunchKind
{
    // Started from the Start menu, a shortcut, or the installer.
    Manual,
    // Started by Windows at sign-in.
    AtSignIn,
    // Restarted by the updater after installing a new version.
    AfterUpdate
}

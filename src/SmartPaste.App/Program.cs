using SmartPaste.App.Infrastructure;
using Velopack;

namespace SmartPaste.App;

internal static class Program
{
    // "SmartPaste.exe --exit" asks the running copy to exit the normal way (so it puts the clipboard back and lets go
    // of held keys), waits until it has, then exits itself. For scripts, installers, and tests.
    public const string ExitArgument = "--exit";

    // Process exit codes.
    public const int ExitOk = 0;
    public const int ExitStartupFailed = 1;
    // --exit: the running copy was still running when the wait ran out.
    public const int ExitStillRunning = 2;

    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

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
        var exitRequested = args.Contains(ExitArgument, StringComparer.OrdinalIgnoreCase);

        Log.Initialize(AppPaths.LogDirectory);
        CrashReporter.Install();

        using var instance = SingleInstance.Acquire();
        if (exitRequested)
        {
            // Nothing running is as good as done.
            return instance.IsPrimary || instance.RequestExit(ExitWait) ? ExitOk : ExitStillRunning;
        }
        if (!instance.IsPrimary)
        {
            instance.SignalPrimary();
            return ExitOk;
        }

        var app = new App(instance);
        app.InitializeComponent();
        return app.RunUntilExit();
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

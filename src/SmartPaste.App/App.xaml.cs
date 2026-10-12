using System.Windows;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;

namespace SmartPaste.App;

// The WPF application: App.xaml's resources, and Smart Paste's run loop. Creating it starts nothing (tests create it
// for its resources); Program.Main calls RunUntilExit.
//
// WPF's Application.Run is not used. It adds a hidden window that shuts the app down as soon as Windows asks whether
// the session may end, even when the user then cancels the restart or sign-out. Smart Paste exits only when the
// session really ends, when something asks it to (the tray's Exit, an update, a copy started with --exit), or when it
// can't start.
public partial class App : Application
{
    private readonly SingleInstance _instance;
    private SessionEndWatcher? _sessionEnd;
    private AppHost? _host;
    private bool _running;
    private bool _exiting;
    private int _exitCode = Program.ExitOk;

    internal App(SingleInstance instance)
    {
        _instance = instance;
    }

    // Runs Smart Paste on this thread until it exits, and returns the process exit code.
    internal int RunUntilExit()
    {
        CrashReporter.Attach(Dispatcher, () => _running, OnStartupFailed);
        // Startup runs inside the message loop, as WPF's own does, so anything it awaits comes back to this thread.
        Dispatcher.InvokeAsync(Start);
        try
        {
            Dispatcher.Run();
        }
        finally
        {
            // ExitNow has done this already, unless a fatal error escaped the message loop: then the keyboard and the
            // user's clipboard are still put back before the process ends.
            DisposeHost();
        }
        return _exitCode;
    }

    private void Start()
    {
        try
        {
            // First, so a restart or sign-out during startup still ends Smart Paste properly.
            _sessionEnd = new SessionEndWatcher(() => ExitNow(Program.ExitOk, "session_end"));
            _instance.OnExitRequested(() => RequestExit("exit_argument"));
            _host = new AppHost(Dispatcher, _instance, () => RequestExit("requested"));
            _host.Start();
            _running = true;
        }
        catch (Exception ex)
        {
            OnStartupFailed(ex);
        }
    }

    // Smart Paste can't run. Left alone, the process would linger with no tray icon, holding the single-instance lock,
    // so every later start would only wake it. Instead: put back whatever was set up, and exit with an error code.
    private void OnStartupFailed(Exception ex)
    {
        Log.Crash(ex, "startup");
        ExitNow(Program.ExitStartupFailed, "startup_failed");
    }

    // Exits once the work in progress on the UI thread is done (the tray's Exit runs inside a menu click, an update
    // inside a button's command). Safe to call from any thread, any number of times.
    private void RequestExit(string reason) => Dispatcher.InvokeAsync(() => ExitNow(Program.ExitOk, reason));

    // UI thread. Puts back everything Smart Paste changed (the user's clipboard, held keys, the tray icon), closes the
    // windows, and ends the message loop so RunUntilExit returns. Runs to the end even when a step fails: the process
    // must end. For a session end it runs inside WM_ENDSESSION, and all of this is done before that returns.
    private void ExitNow(int exitCode, string reason)
    {
        if (_exiting) return;
        _exiting = true;
        _exitCode = exitCode;
        Log.Event("exit", ("reason", reason));
        // The host first: it closes its own windows (flyout, picker, chips). What is left is the settings window and
        // the tour, whose last save then reaches nothing that is already shut down.
        DisposeHost();
        CloseWindows();
        // After the current message, since this may be running inside the session window's own window procedure.
        if (_sessionEnd is { } sessionEnd) Dispatcher.InvokeAsync(sessionEnd.Dispose, DispatcherPriority.Send);
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    private void DisposeHost()
    {
        var host = _host;
        _host = null;
        if (host is null) return;
        try
        {
            host.Dispose();
        }
        catch (Exception ex)
        {
            Log.Crash(ex, "exit");
        }
    }

    // The settings window and the welcome tour, closed as if by hand (so a tour left open counts as seen, as before).
    private void CloseWindows()
    {
        foreach (var window in Windows.Cast<Window>().ToArray())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Crash(ex, "exit");
            }
        }
    }
}

using System.Windows;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App;

// Composition root: creates every long-lived piece of the app, wires them together, and tears them down in
// reverse order on exit.
internal sealed class AppHost : IDisposable
{
    private readonly Application _application;
    private readonly SingleInstance _instance;

    public AppHost(Application application, SingleInstance instance)
    {
        _application = application;
        _instance = instance;
        Settings = new SettingsStore(AppPaths.SettingsFile);
        State = new AppState();
        Controller = new AppController(State, Settings, application.Dispatcher);
    }

    public SettingsStore Settings { get; }

    public AppState State { get; }

    public AppController Controller { get; }

    public void Start()
    {
        Log.Event("app_start", ("version", AppInfo.Version));
        if (Settings.IsFirstRun) Settings.SaveNow();
        StartupRegistration.Apply(Settings.Current.StartAtLogin);
        Controller.ExitRequested += (_, _) => _application.Shutdown();
    }

    public void Dispose()
    {
        Controller.Dispose();
        Log.Event("app_exit");
    }
}

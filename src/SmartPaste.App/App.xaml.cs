using System.Windows;
using SmartPaste.App.Infrastructure;

namespace SmartPaste.App;

public partial class App : Application
{
    private readonly SingleInstance _instance;
    private AppHost? _host;

    internal App(SingleInstance instance)
    {
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashReporter.Attach(Dispatcher);
        _host = new AppHost(this, _instance);
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}

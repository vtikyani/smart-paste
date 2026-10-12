using System.Windows.Threading;
using SmartPaste.App;
using SmartPaste.App.Infrastructure;

// Runs WPF code the way the app does: on one long-lived STA thread with a running dispatcher and the real App
// object, so App.xaml's resources (Fluent theme, brand dictionaries, control styles) resolve exactly as in the app.
// WPF allows one Application per process, so every UI test shares this one. The app's startup (AppHost) never runs:
// that only happens in Application.Run.
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Action action) => Ui.Value.Invoke(action);

    public static T Run<T>(Func<T> function) => Ui.Value.Invoke(function);

    public static Task RunAsync(Func<Task> function) => Ui.Value.InvokeAsync(function).Task.Unwrap();

    public static Task<T> RunAsync<T>(Func<Task<T>> function) => Ui.Value.InvokeAsync(function).Task.Unwrap();

    // Lets queued dispatcher work (layout, bindings, timers that are due) run.
    public static void DoEvents() => Ui.Value.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // The single-instance lock is per data folder; TestEnvironment points that at a temp folder.
                var app = new App(SingleInstance.Acquire());
                app.InitializeComponent();
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ready.Set();
            }
            if (failure is null) Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test UI"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure is not null) throw new InvalidOperationException("The WPF test host could not start.", failure);
        return dispatcher!;
    }
}

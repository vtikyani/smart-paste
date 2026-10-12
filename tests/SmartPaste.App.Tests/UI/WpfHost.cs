using System.Windows;
using System.Windows.Threading;
using SmartPaste.App;
using SmartPaste.App.Infrastructure;

// The one UI thread every UI test runs on: a long-lived STA thread with a running dispatcher and the real App object,
// so App.xaml's resources (Fluent theme, brand dictionaries, control styles) resolve exactly as in the app. WPF allows
// one Application per process, so this is the only place a test creates it; UiTestHost runs on this same thread.
// Creating App starts nothing: the app itself only runs from Program.Main.
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dispatcher Dispatcher => Ui.Value;

    public static void Run(Action action) => Ui.Value.Invoke(action);

    public static T Run<T>(Func<T> function) => Ui.Value.Invoke(function);

    public static Task RunAsync(Func<Task> function) => Ui.Value.InvokeAsync(function).Task.Unwrap();

    public static Task<T> RunAsync<T>(Func<Task<T>> function) => Ui.Value.InvokeAsync(function).Task.Unwrap();

    // Lets queued dispatcher work (layout, bindings, timers that are due) run.
    public static void DoEvents() => Ui.Value.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static Dispatcher Start()
    {
        // Never a second Application: if one exists already, its thread is the UI thread.
        if (Application.Current is { } existing) return existing.Dispatcher;

        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // The single-instance lock is per data folder; TestEnvironment points that at a temp folder.
                var app = new App(SingleInstance.Acquire());
                app.InitializeComponent();
                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                ready.SetException(new InvalidOperationException("The WPF test host could not start.", ex));
                return;
            }
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test UI"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartPaste.App;
using SmartPaste.App.Infrastructure;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SmartPaste.App.Tests.Settings;

// Runs UI tests on one long-lived STA thread that owns the app's real Application object, so windows and pages
// load the same resources (WPF-UI themes, Brand tokens, brand art) as the shipped app. WPF allows only one
// Application per process, so every UI test shares it.
internal static class UiTestHost
{
    // Set this to a folder to also save every rendered window as a PNG, to look at them.
    public const string SnapshotFolderVariable = "SMARTPASTE_SNAPSHOT_DIR";

    // Set this to docs/images to refresh the screenshots the README shows (only those are written).
    public const string ReadmeFolderVariable = "SMARTPASTE_README_IMAGES";

    private static readonly HashSet<string> ReadmeImages = new(StringComparer.Ordinal)
    {
        "onboarding-1-welcome-light",
        "onboarding-2-howitworks-dark",
        "onboarding-practice-filled-dark",
        "settings-general-light",
        "settings-jev-dark",
        "settings-shortcuts-light"
    };

    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dispatcher UiDispatcher => Ui.Value;

    public static void Run(Action test) => Run(() =>
    {
        test();
        return Task.CompletedTask;
    });

    // Runs the test on the UI thread and waits for it; async continuations stay on the UI thread.
    public static void Run(Func<Task> test)
    {
        var operation = Ui.Value.InvokeAsync(test);
        operation.Task.Unwrap().GetAwaiter().GetResult();
    }

    // Lets queued work (bindings, layout, Loaded events, posted callbacks) run before the test continues.
    public static async Task Settle(int milliseconds = 0)
    {
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (milliseconds > 0) await Task.Delay(milliseconds);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    public static void UseTheme(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var brand = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith("Brand.Dark.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            d.Source?.OriginalString.EndsWith("Brand.Light.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/SmartPaste;component/UI/Theme/Brand.{(theme == ApplicationTheme.Dark ? "Dark" : "Light")}.xaml")
        };
        if (brand is null) dictionaries.Add(replacement);
        else dictionaries[dictionaries.IndexOf(brand)] = replacement;
    }

    // Shows a window far off-screen without activating it, so it never appears on the desktop or takes focus,
    // but still loads, lays out, and renders like a real window.
    public static void ShowOffScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
    }

    // Renders a window's content over a flat stand-in for Mica (which only the desktop compositor can draw).
    public static BitmapSource Render(Window window, double scale = 1)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var size = new Size(content.ActualWidth, content.ActualHeight);
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var backdrop = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(backdrop, null, new Rect(size));
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // Saves a rendered window when SMARTPASTE_SNAPSHOT_DIR or (for README images) SMARTPASTE_README_IMAGES is set.
    public static void SaveSnapshot(BitmapSource bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable(SnapshotFolderVariable) is { Length: > 0 } folder) SavePng(bitmap, folder, name);
        if (ReadmeImages.Contains(name) && Environment.GetEnvironmentVariable(ReadmeFolderVariable) is { Length: > 0 } readme)
            SavePng(bitmap, readme, name);
    }

    private static void SavePng(BitmapSource bitmap, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    // True when the bitmap has visible variety, i.e. something was actually drawn.
    public static bool HasContent(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var distinct = new HashSet<int>();
        for (var i = 0; i < pixels.Length && distinct.Count < 16; i += 4 * 97)
            distinct.Add(BitConverter.ToInt32(pixels, i));
        return distinct.Count >= 16;
    }

    private static Dispatcher Start()
    {
        if (Application.Current is { } existing) return existing.Dispatcher;

        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // The real app object, so App.xaml's dictionaries load exactly as in the product. Its host
                // (keyboard hook, tray) only starts in OnStartup, which never runs here.
                var app = new App(SingleInstance.Acquire());
                app.InitializeComponent();
                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "UI tests"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}

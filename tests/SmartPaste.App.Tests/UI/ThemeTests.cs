using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.UI.Theme;
using Wpf.Ui.Appearance;

// The brand dictionaries, the shared logo resources other windows use, and the theme service that swaps them.
public sealed class ThemeTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-theme-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private static ResourceDictionary Load(string file) =>
        new() { Source = new Uri($"pack://application:,,,/SmartPaste;component/UI/Theme/{file}", UriKind.Absolute) };

    [Fact]
    public void EveryThemeDefinesTheSameBrandKeys() => WpfHost.Run(() =>
    {
        string[] Keys(ResourceDictionary d) => d.Keys.Cast<object>().Select(k => k.ToString()!).Order(StringComparer.Ordinal).ToArray();
        string[] BrandKeys(ResourceDictionary d) => Keys(d).Where(k => k.StartsWith("Brand.", StringComparison.Ordinal)).ToArray();
        // Light and dark also restyle WPF-UI's own brushes, the same ones each. High contrast leaves those to
        // WPF-UI's high contrast theme, which uses the system colors.
        var light = Keys(Load("Brand.Light.xaml"));
        Assert.Equal(light, Keys(Load("Brand.Dark.xaml")));
        Assert.Equal(BrandKeys(Load("Brand.Light.xaml")), BrandKeys(Load("Brand.HighContrast.xaml")));
        Assert.Contains("Brand.LogoMonoImage", light);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", light);
    });

    [Fact]
    public void LogoImagesAreAvailableToEveryWindow() => WpfHost.Run(() =>
    {
        var logo = Assert.IsType<DrawingImage>(Application.Current.FindResource("Brand.LogoImage"));
        var mono = Assert.IsType<DrawingImage>(Application.Current.FindResource("Brand.LogoMonoImage"));
        // Square frames, so the marks never stretch or shift when sized.
        Assert.Equal(logo.Width, logo.Height);
        Assert.Equal(mono.Width, mono.Height);
    });

    [Fact]
    public void ThemeServiceFollowsTheSetting() => WpfHost.Run(() =>
    {
        var settings = new SettingsStore(_settingsPath);
        using var signals = new SystemSignals(Dispatcher.CurrentDispatcher);
        using var theme = new ThemeService(Application.Current, settings, signals);
        theme.Start();

        settings.Update(s => s with { Theme = ThemePreference.Light });
        Assert.Equal(ApplicationTheme.Light, theme.Current);
        Assert.Equal(ApplicationTheme.Light, ApplicationThemeManager.GetAppTheme());
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), Brush("Brand.PopupSurfaceBrush"));
        Assert.Equal(Color.FromRgb(0x0E, 0x0E, 0x12), MonoLogoColor());
        // WPF-UI's own brushes wear the brand palette, and the accent is periwinkle, not the Windows accent.
        Assert.Equal(Color.FromRgb(0xF7, 0xF7, 0xF9), Brush("ApplicationBackgroundBrush"));
        Assert.Equal(Color.FromRgb(0x5B, 0x63, 0xE6), Brush("AccentFillColorDefaultBrush"));

        settings.Update(s => s with { Theme = ThemePreference.Dark });
        Assert.Equal(ApplicationTheme.Dark, theme.Current);
        Assert.Equal(Color.FromArgb(0xF2, 0x0E, 0x0E, 0x12), Brush("Brand.PopupSurfaceBrush"));
        Assert.Equal(Colors.White, MonoLogoColor());
        Assert.Equal(Color.FromRgb(0x0B, 0x0B, 0x0E), Brush("ApplicationBackgroundBrush"));
        Assert.Equal(Color.FromRgb(0x8B, 0x9B, 0xFF), Brush("AccentFillColorDefaultBrush"));

        // Swapping replaces the theme dictionary; it never stacks a second one.
        string[] themed = ["Brand.Light.xaml", "Brand.Dark.xaml", "Brand.HighContrast.xaml"];
        Assert.Equal(1, Application.Current.Resources.MergedDictionaries.Count(d =>
            d.Source?.OriginalString is { } source && themed.Any(name => source.EndsWith(name, StringComparison.Ordinal))));
    });

    [Fact]
    public void BindingErrorCollectorCatchesErrors() => WpfHost.Run(() =>
    {
        // Guards the render tests: they rely on this collector to fail on broken bindings.
        using var errors = new BindingErrors();
        var text = new TextBlock { DataContext = new { Present = "yes" } };
        text.SetBinding(TextBlock.TextProperty, "Missing");
        text.Measure(new Size(100, 100));
        Assert.Contains("Missing", errors.Text, StringComparison.Ordinal);
    });

    private static Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private static Color MonoLogoColor()
    {
        var image = (DrawingImage)Application.Current.FindResource("Brand.LogoMonoImage");
        var glyph = (GeometryDrawing)((DrawingGroup)image.Drawing).Children[1];
        return ((SolidColorBrush)glyph.Brush).Color;
    }
}

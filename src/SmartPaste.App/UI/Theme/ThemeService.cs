using System.Windows;
using Microsoft.Win32;
using SmartPaste.App.Infrastructure;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Theme;

// Applies the app theme from settings (System, Light, or Dark): WPF-UI's Fluent resources with the Windows accent
// color, plus the matching brand dictionary (Brand.Light.xaml, Brand.Dark.xaml, or Brand.HighContrast.xaml).
// When set to System it follows Windows live, including high contrast and accent color changes.
internal sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightBrand = BrandUri("Brand.Light.xaml");
    private static readonly Uri DarkBrand = BrandUri("Brand.Dark.xaml");
    private static readonly Uri HighContrastBrand = BrandUri("Brand.HighContrast.xaml");

    private readonly Application _application;
    private readonly SettingsStore _settings;
    private readonly SystemSignals _signals;
    private readonly Dictionary<Uri, ResourceDictionary> _brandDictionaries = [];
    private bool _started;

    public ThemeService(Application application, SettingsStore settings, SystemSignals signals)
    {
        _application = application;
        _settings = settings;
        _signals = signals;
    }

    public ApplicationTheme Current { get; private set; } = ApplicationTheme.Unknown;

    // Raised on the UI thread after the theme or accent color changed.
    public event EventHandler? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        Apply();
        _settings.Changed += OnSettingsChanged;
        _signals.AppearanceChanged += OnAppearanceChanged;
    }

    // What a preference resolves to right now. High contrast always wins: it is an accessibility setting.
    public static ApplicationTheme Resolve(ThemePreference preference)
    {
        if (SystemParameters.HighContrast) return ApplicationTheme.HighContrast;
        return preference switch
        {
            ThemePreference.Light => ApplicationTheme.Light,
            ThemePreference.Dark => ApplicationTheme.Dark,
            _ => AppsUseLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark
        };
    }

    private void Apply()
    {
        var theme = Resolve(_settings.Current.Theme);

        // WPF-UI also restyles Application.MainWindow's backdrop. That can be any window, including a transparent
        // popup, which a backdrop would break; Fluent windows are updated explicitly below instead.
        var mainWindow = _application.MainWindow;
        _application.MainWindow = null;
        try
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        }
        finally
        {
            _application.MainWindow = mainWindow;
        }

        SwapBrandDictionary(theme);
        foreach (Window window in _application.Windows)
        {
            if (window is FluentWindow fluent && fluent.IsLoaded)
                WindowBackgroundManager.UpdateBackground(fluent, theme, fluent.WindowBackdropType);
        }

        Current = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SwapBrandDictionary(ApplicationTheme theme)
    {
        var wanted = theme switch
        {
            ApplicationTheme.HighContrast => HighContrastBrand,
            ApplicationTheme.Light => LightBrand,
            _ => DarkBrand
        };
        var merged = _application.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (IsBrandThemeDictionary(merged[i].Source)) index = i;
        }
        // The high contrast dictionary reads the system colors when it loads, so it is reloaded on every change
        // rather than cached; the light and dark ones never change and are loaded once.
        var isHighContrast = wanted == HighContrastBrand;
        if (index >= 0 && !isHighContrast && SameDictionary(merged[index].Source, wanted)) return;

        ResourceDictionary? dictionary;
        if (isHighContrast)
        {
            dictionary = new ResourceDictionary { Source = wanted };
        }
        else if (!_brandDictionaries.TryGetValue(wanted, out dictionary))
        {
            dictionary = new ResourceDictionary { Source = wanted };
            _brandDictionaries[wanted] = dictionary;
        }
        if (index >= 0) merged[index] = dictionary;
        else merged.Add(dictionary);
    }

    private static bool IsBrandThemeDictionary(Uri? source)
    {
        var path = source?.OriginalString ?? "";
        return path.EndsWith("Brand.Light.xaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Brand.Dark.xaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Brand.HighContrast.xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDictionary(Uri? source, Uri wanted)
    {
        var name = wanted.OriginalString[(wanted.OriginalString.LastIndexOf('/') + 1)..];
        return source?.OriginalString.EndsWith(name, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Uri BrandUri(string file) => new($"pack://application:,,,/SmartPaste;component/UI/Theme/{file}", UriKind.Absolute);

    private static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        // Windows treats a missing value as light.
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.Theme == current.Theme) return;
        if (_application.Dispatcher.CheckAccess()) Apply();
        else _application.Dispatcher.BeginInvoke(Apply);
    }

    // Light/dark, accent, and high contrast changes all go through the same path; with a fixed Light or Dark
    // preference this still refreshes the accent color and honors high contrast.
    private void OnAppearanceChanged(object? sender, EventArgs e) => Apply();

    public void Dispose()
    {
        if (!_started) return;
        _settings.Changed -= OnSettingsChanged;
        _signals.AppearanceChanged -= OnAppearanceChanged;
    }
}

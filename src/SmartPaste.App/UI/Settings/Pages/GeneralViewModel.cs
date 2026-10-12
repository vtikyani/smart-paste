using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal sealed record ThemeChoice(ThemePreference Value, string Label);

internal sealed record LifetimeChoice(int Minutes, string Label);

// The General page: on/off, pause, start-up, theme, and how pasting behaves.
internal sealed partial class GeneralViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;

    // True while values are copied in from settings or app state, so those copies aren't saved straight back.
    private bool _loading;

    public GeneralViewModel(SettingsContext context)
    {
        _context = context;
        Lifetimes = AppSettings.LifetimeChoices.Select(m => new LifetimeChoice(m, StatusText.Minutes(m))).ToArray();
        Load();
        context.Services.Settings.Changed += OnSettingsChanged;
        context.Services.State.PropertyChanged += OnStateChanged;
    }

    public IReadOnlyList<ThemeChoice> Themes { get; } =
    [
        new(ThemePreference.System, "Use my Windows setting"),
        new(ThemePreference.Light, "Light"),
        new(ThemePreference.Dark, "Dark")
    ];

    public IReadOnlyList<LifetimeChoice> Lifetimes { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string EnabledDescription { get; set; } = "";

    // "Paused until 3:40 PM", or null when Smart Paste isn't paused.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    public partial string? PauseText { get; set; }

    public bool IsPaused => PauseText is not null;

    [ObservableProperty]
    public partial bool StartAtLogin { get; set; }

    [ObservableProperty]
    public partial ThemeChoice? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial LifetimeChoice? SelectedLifetime { get; set; }

    [ObservableProperty]
    public partial bool ShowFieldHints { get; set; }

    [ObservableProperty]
    public partial bool ShowPasteConfirmation { get; set; }

    [ObservableProperty]
    public partial bool NotifyWhenArmed { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading) _context.TrySave(() => _context.Services.Controller.SetEnabled(value));
    }

    partial void OnStartAtLoginChanged(bool value)
    {
        if (_loading) return;
        if (_context.TrySave(s => s with { StartAtLogin = value })) ApplyStartAtLogin(value);
    }

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (!_loading && value is not null) _context.TrySave(s => s with { Theme = value.Value });
    }

    partial void OnSelectedLifetimeChanged(LifetimeChoice? value)
    {
        if (!_loading && value is not null) _context.TrySave(s => s with { ProfileLifetimeMinutes = value.Minutes });
    }

    partial void OnShowFieldHintsChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { ShowFieldHints = value });
    }

    partial void OnShowPasteConfirmationChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { ShowPasteConfirmation = value });
    }

    partial void OnNotifyWhenArmedChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { NotifyWhenArmed = value });
    }

    [RelayCommand]
    private void Resume() => _context.TrySave(_context.Services.Controller.Resume);

    public void Dispose()
    {
        _context.Services.Settings.Changed -= OnSettingsChanged;
        _context.Services.State.PropertyChanged -= OnStateChanged;
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current) => _context.Post(Load);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.IsEnabled) or nameof(AppState.PausedUntil)) Load();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _context.Settings;
            var state = _context.Services.State;
            IsEnabled = state.IsEnabled;
            PauseText = state.IsEnabled && state.PausedUntil is { } until
                ? StatusText.PausedUntil(until, DateTimeOffset.Now)
                : null;
            EnabledDescription = !state.IsEnabled
                ? "Ctrl+V pastes normally, and Smart Paste doesn't look at what you copy."
                : PauseText is { } paused
                    ? paused + ". Ctrl+V pastes normally until then."
                    : "Ctrl+V pastes the right value into each form field.";
            // The real start-up entry, since it can be removed or switched off outside Smart Paste.
            StartAtLogin = StartupRegistration.IsEnabledOr(settings.StartAtLogin);
            SelectedTheme = Themes.First(t => t.Value == settings.Theme);
            SelectedLifetime = Lifetimes.FirstOrDefault(l => l.Minutes == settings.ProfileLifetimeMinutes);
            ShowFieldHints = settings.ShowFieldHints;
            ShowPasteConfirmation = settings.ShowPasteConfirmation;
            NotifyWhenArmed = settings.NotifyWhenArmed;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyStartAtLogin(bool enabled)
    {
        try
        {
            StartupRegistration.Apply(enabled);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("startup_apply_error", ex);
            _context.Notice.Show(InfoBarSeverity.Warning, "Couldn't change how Smart Paste starts",
                "Windows didn't allow the change. You can turn Smart Paste on or off in Settings > Apps > Startup.");
            Load();
        }
    }
}

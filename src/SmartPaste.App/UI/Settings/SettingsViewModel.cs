using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Settings;

// The status chip at the bottom of the sidebar: its dot's color.
internal enum StatusTone { Ready, Armed, Busy, Paused, Off, Attention }

// The settings window itself: the live status card and the notice banner. Pages have their own view models.
internal sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;

    public SettingsViewModel(SettingsContext context)
    {
        _context = context;
        Refresh();
        context.Services.State.PropertyChanged += OnStateChanged;
    }

    // Asks the window to show a page (one of SettingsPages).
    public event Action<string>? PageRequested;

    public Notice Notice => _context.Notice;

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusDetail { get; set; } = "";

    [ObservableProperty]
    public partial StatusTone StatusTone { get; set; }

    // The page that explains or fixes the current status.
    [RelayCommand]
    private void OpenStatusPage() =>
        PageRequested?.Invoke(StatusTone == StatusTone.Attention ? SettingsPages.Jev : SettingsPages.General);

    public void Dispose() => _context.Services.State.PropertyChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var state = _context.Services.State;
        (StatusTone, StatusTitle, StatusDetail) = state.Status switch
        {
            TrayStatus.Off => (StatusTone.Off, "Smart Paste is off", "Ctrl+V pastes normally"),
            TrayStatus.Paused => (StatusTone.Paused, "Paused",
                state.PausedUntil is { } until ? "Until " + StatusText.Moment(until, DateTimeOffset.Now) : "Ctrl+V pastes normally"),
            TrayStatus.Attention => (StatusTone.Attention, "Needs your attention", "A key isn't working"),
            TrayStatus.Parsing => (StatusTone.Busy, "Reading your copy", "Just a moment"),
            TrayStatus.Armed => (StatusTone.Armed, "Ready to paste",
                StatusText.Count(state.Armed?.FieldCount ?? 0, "field", "fields") + " from your copy"),
            _ => (StatusTone.Ready, "Smart Paste is on", "Copy a block of text to start")
        };
    }
}

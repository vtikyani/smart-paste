using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;

namespace SmartPaste.App.UI.Settings.Pages;

// Ready: a new version is on this PC and installs the next time Smart Paste starts, or now with Restart now.
internal enum UpdateStage { Unsupported, Idle, Checking, UpToDate, Downloading, Ready, Restarting, Failed }

// The About page: version, updates, links, and the welcome tour.
//
// Updates work like the automatic check (UpdateScheduler): a new version is downloaded in the background, and the
// installer applies it the next time Smart Paste starts. AppState.Update is only ever set once it is on this PC.
internal sealed partial class AboutViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading;
    private AvailableUpdate? _ready;

    public AboutViewModel(SettingsContext context)
    {
        _context = context;
        if (!context.Services.Updates.IsSupported)
            Show(UpdateStage.Unsupported, "This copy wasn't installed with the installer, so it can't update itself.");
        else if (context.Services.State.Update is { } waiting)
            ShowReady(waiting);
        else
            Show(UpdateStage.Idle, "");
        Load();
        context.Services.Settings.Changed += OnSettingsChanged;
        context.Services.State.PropertyChanged += OnStateChanged;
    }

    public string Version => AppInfo.Version;

    public string Copyright { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    public bool HasRepository => AppInfo.RepositoryUrl.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking), nameof(IsDownloading), nameof(CanCheck), nameof(CanRestart), nameof(CheckText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(RestartCommand))]
    public partial UpdateStage Stage { get; set; }

    // What's happening with updates, in one sentence; empty before the first check.
    [ObservableProperty]
    public partial string UpdateMessage { get; set; } = "";

    // What's new in the version that is ready, as plain text; empty when it has no notes.
    [ObservableProperty]
    public partial string ReleaseNotes { get; private set; } = "";

    [ObservableProperty]
    public partial int DownloadPercent { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    public bool IsChecking => Stage == UpdateStage.Checking;

    public bool IsDownloading => Stage == UpdateStage.Downloading;

    public bool CanCheck => Stage is UpdateStage.Idle or UpdateStage.UpToDate or UpdateStage.Failed;

    public bool CanRestart => Stage == UpdateStage.Ready;

    public string CheckText => Stage switch
    {
        UpdateStage.UpToDate => "Check again",
        UpdateStage.Failed => "Try again",
        _ => "Check for updates"
    };

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { CheckForUpdates = value });
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        Show(UpdateStage.Checking, "Checking for updates…");
        try
        {
            var result = await _context.Services.Updates.CheckAsync(_lifetime.Token);
            // The automatic check delivered a version meanwhile; that one stands.
            if (Stage != UpdateStage.Checking) return;
            switch (result.Status)
            {
                case UpdateCheckStatus.Available when result.Update is { } update:
                    await DownloadAsync(update);
                    break;
                case UpdateCheckStatus.UpToDate:
                    Show(UpdateStage.UpToDate, $"You're up to date. Checked at {StatusText.Moment(DateTimeOffset.Now, DateTimeOffset.Now)}.");
                    break;
                default:
                    Show(UpdateStage.Failed, "Couldn't check for updates. Check your internet connection, then try again.");
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    // A version found here is downloaded right away, as the automatic check does, so it too installs at the next start.
    private async Task DownloadAsync(AvailableUpdate update)
    {
        DownloadPercent = 0;
        Show(UpdateStage.Downloading, $"Downloading Smart Paste {update.Version}…");
        var progress = new Progress<int>(percent => DownloadPercent = Math.Clamp(percent, 0, 100));
        if (!await _context.Services.Updates.DownloadAsync(update, progress, _lifetime.Token))
        {
            Show(UpdateStage.Failed, "Couldn't download the update. Check your internet connection, then try again.");
            return;
        }
        ShowReady(update);
        // Share it with the tray, which shows that an update is waiting.
        _context.Services.State.Update = update;
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (_ready is not { } update) return;
        Show(UpdateStage.Restarting, "Restarting…");
        try
        {
            // The update is on this PC already; this only makes sure (the package is found and kept).
            if (!await _context.Services.Updates.DownloadAsync(update, progress: null, _lifetime.Token) ||
                !_context.Services.Updates.ApplyAndRestart(update))
                Show(UpdateStage.Failed, "Couldn't install the update. Try again, or download it from the website.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private void OpenSource() => _context.OpenUrl(AppInfo.RepositoryUrl);

    [RelayCommand]
    private void OpenPrivacy() => _context.OpenUrl(AppInfo.RepositoryUrl + "/blob/main/PRIVACY.md");

    [RelayCommand]
    private void OpenLicenses() => _context.OpenUrl(AppInfo.RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.md");

    [RelayCommand]
    private void ReportProblem() => _context.OpenUrl(AppInfo.RepositoryUrl + "/issues");

    [RelayCommand]
    private void ShowWelcomeTour() => _context.Navigator.ShowOnboarding();

    public void Dispose()
    {
        _context.Services.Settings.Changed -= OnSettingsChanged;
        _context.Services.State.PropertyChanged -= OnStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void ShowReady(AvailableUpdate update)
    {
        _ready = update;
        Show(UpdateStage.Ready, $"Version {update.Version} is ready. It installs the next time Smart Paste starts.");
        ReleaseNotes = ReleaseNotesText.ToPlainText(update.ReleaseNotesMarkdown);
    }

    private void Show(UpdateStage stage, string message)
    {
        UpdateMessage = message;
        Stage = stage;
        if (stage is not (UpdateStage.Ready or UpdateStage.Restarting)) ReleaseNotes = "";
    }

    // The automatic check found and downloaded a version while this page was open.
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppState.Update)) return;
        _context.Post(() =>
        {
            if (_context.Services.State.Update is { } update && !ReferenceEquals(update, _ready) &&
                Stage is UpdateStage.Idle or UpdateStage.Checking or UpdateStage.UpToDate or UpdateStage.Failed)
                ShowReady(update);
        });
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current) => _context.Post(Load);

    private void Load()
    {
        _loading = true;
        try
        {
            CheckForUpdates = _context.Settings.CheckForUpdates;
        }
        finally
        {
            _loading = false;
        }
    }
}

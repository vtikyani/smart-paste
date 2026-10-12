using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Settings.Pages;

internal enum UpdateStage { Unsupported, Idle, Checking, UpToDate, Available, Downloading, Restarting, Failed }

// The About page: version, updates, links, and the welcome tour.
internal sealed partial class AboutViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading;
    private AvailableUpdate? _available;

    public AboutViewModel(SettingsContext context)
    {
        _context = context;
        if (!context.Services.Updates.IsSupported)
            Show(UpdateStage.Unsupported, "This copy wasn't installed with the installer, so it can't update itself.");
        else if (context.Services.State.Update is { } waiting)
            ShowAvailable(waiting);
        else
            Show(UpdateStage.Idle, "");
        Load();
        context.Services.Settings.Changed += OnSettingsChanged;
    }

    public string Version => AppInfo.Version;

    public string Copyright { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    public bool HasRepository => AppInfo.RepositoryUrl.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking), nameof(IsDownloading), nameof(CanCheck), nameof(CanInstall), nameof(CheckText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand))]
    public partial UpdateStage Stage { get; set; }

    // What's happening with updates, in one sentence; empty before the first check.
    [ObservableProperty]
    public partial string UpdateMessage { get; set; } = "";

    [ObservableProperty]
    public partial int DownloadPercent { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    public bool IsChecking => Stage == UpdateStage.Checking;

    public bool IsDownloading => Stage == UpdateStage.Downloading;

    public bool CanCheck => Stage is UpdateStage.Idle or UpdateStage.UpToDate or UpdateStage.Failed;

    public bool CanInstall => Stage == UpdateStage.Available;

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
            var update = await _context.Services.Updates.CheckAsync(_lifetime.Token);
            if (update is null)
            {
                Show(UpdateStage.UpToDate, $"You're up to date. Checked at {StatusText.Moment(DateTimeOffset.Now, DateTimeOffset.Now)}.");
                return;
            }
            // Share it with the tray, which shows that an update is waiting.
            _context.Services.State.Update = update;
            ShowAvailable(update);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The update service reports through exceptions; any of them means "no answer this time".
            Log.Error("update_check_error", ex);
            Show(UpdateStage.Failed, "Couldn't check for updates. Check your internet connection, then try again.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (_available is not { } update) return;
        DownloadPercent = 0;
        Show(UpdateStage.Downloading, $"Downloading Smart Paste {update.Version}. It restarts by itself when it's done.");
        try
        {
            var progress = new Progress<int>(percent => DownloadPercent = Math.Clamp(percent, 0, 100));
            await _context.Services.Updates.DownloadAsync(update, progress, _lifetime.Token);
            Show(UpdateStage.Restarting, "Restarting…");
            _context.Services.Updates.ApplyAndRestart(update);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Error("update_install_error", ex);
            _available = null;
            Show(UpdateStage.Failed, "Couldn't download the update. Check your internet connection, then try again.");
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
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void ShowAvailable(AvailableUpdate update)
    {
        _available = update;
        Show(UpdateStage.Available, $"Version {update.Version} is available. Smart Paste downloads it, then restarts.");
    }

    private void Show(UpdateStage stage, string message)
    {
        UpdateMessage = message;
        Stage = stage;
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

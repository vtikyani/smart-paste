using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.UI.Navigation;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

// An app in one of the privacy page's lists. Name and icon fill in when the app is running.
internal sealed partial class AppItem : ObservableObject
{
    public AppItem(string exeName)
    {
        ExeName = exeName;
    }

    public string ExeName { get; }

    // The app's own name ("Visual Studio Code"), known while it's running.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Detail))]
    public partial string? DisplayName { get; set; }

    // The app's name when known, otherwise its program name.
    public string Title => DisplayName ?? ExeName;

    // The program name under the app's name, or "" when the title is the program name already.
    public string Detail => DisplayName is null ? "" : ExeName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial ImageSource? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    public void Describe(RunningApp app)
    {
        DisplayName = app.DisplayName;
        Icon = app.Icon;
    }
}

// The Privacy page: what leaves the PC, screenshots, apps Smart Paste leaves alone, and logs.
internal sealed partial class PrivacyViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading;

    public PrivacyViewModel(SettingsContext context)
    {
        _context = context;
        ProtectedApps = context.Services.ProtectedApps
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(exe => new AppItem(exe))
            .ToArray();
        Load();
        context.Services.Settings.Changed += OnSettingsChanged;
        _ = DescribeAsync(ProtectedApps);
    }

    public IReadOnlyList<AppItem> ProtectedApps { get; }

    public ObservableCollection<AppItem> ExcludedApps { get; } = [];

    // Running apps that can still be added, loaded when the "Add an app" section opens.
    public ObservableCollection<RunningApp> RunningApps { get; } = [];

    [ObservableProperty]
    public partial bool ReadScreenshots { get; set; }

    // "15 minutes": how long a copied block stays ready, for the "nothing else" statement.
    [ObservableProperty]
    public partial string BlockLifetime { get; set; } = "";

    [ObservableProperty]
    public partial bool HasExcludedApps { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddButtonText))]
    public partial bool IsAddingApp { get; set; }

    public string AddButtonText => IsAddingApp ? "Done" : "Add an app";

    [ObservableProperty]
    public partial bool IsLoadingRunningApps { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTypedAppCommand))]
    public partial string NewAppName { get; set; } = "";

    [ObservableProperty]
    public partial string AddError { get; set; } = "";

    partial void OnReadScreenshotsChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { ReadScreenshots = value });
    }

    // Opening the section lists what's running right now.
    partial void OnIsAddingAppChanged(bool value)
    {
        if (value) _ = LoadRunningAppsAsync();
        else AddError = "";
    }

    partial void OnNewAppNameChanged(string value) => AddError = "";

    [RelayCommand]
    private void ToggleAdding() => IsAddingApp = !IsAddingApp;

    private bool CanAddTypedApp() => !string.IsNullOrWhiteSpace(NewAppName);

    [RelayCommand(CanExecute = nameof(CanAddTypedApp))]
    private void AddTypedApp()
    {
        if (!ExcludedAppNames.TryNormalize(NewAppName, _context.Settings.ExcludedApps, _context.Services.ProtectedApps,
                out var exe, out var error))
        {
            AddError = error;
            return;
        }
        if (Add(exe)) NewAppName = "";
    }

    [RelayCommand]
    private void AddRunningApp(RunningApp app)
    {
        if (!ExcludedAppNames.TryNormalize(app.ExeName, _context.Settings.ExcludedApps, _context.Services.ProtectedApps,
                out var exe, out var error))
        {
            AddError = error;
            return;
        }
        if (Add(exe)) RunningApps.Remove(app);
    }

    [RelayCommand]
    private void RemoveApp(AppItem app) =>
        _context.TrySave(s => s with
        {
            ExcludedApps = s.ExcludedApps.Where(a => !a.Equals(app.ExeName, StringComparison.OrdinalIgnoreCase)).ToArray()
        });

    [RelayCommand]
    private async Task RefreshRunningAppsAsync() => await LoadRunningAppsAsync();

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            _context.Services.Shell.OpenFolder(Log.Directory);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Error("open_logs_error", ex);
            _context.Notice.Show(InfoBarSeverity.Warning, "Couldn't open the log folder",
                "You can find it here: " + Log.Directory);
        }
    }

    public void Dispose()
    {
        _context.Services.Settings.Changed -= OnSettingsChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private bool Add(string exe)
    {
        if (!_context.TrySave(s => s with { ExcludedApps = [.. s.ExcludedApps, exe] })) return false;
        Log.Event("app_excluded", ("app", exe));
        return true;
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current) => _context.Post(Load);

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _context.Settings;
            ReadScreenshots = settings.ReadScreenshots;
            BlockLifetime = StatusText.Minutes(settings.ProfileLifetimeMinutes);
            SyncExcludedApps(settings.ExcludedApps);
        }
        finally
        {
            _loading = false;
        }
    }

    // Updates the list in place, so rows that stay keep their icons and the list doesn't jump.
    private void SyncExcludedApps(IReadOnlyList<string> saved)
    {
        for (var i = ExcludedApps.Count - 1; i >= 0; i--)
            if (!saved.Contains(ExcludedApps[i].ExeName, StringComparer.OrdinalIgnoreCase)) ExcludedApps.RemoveAt(i);

        var added = new List<AppItem>();
        for (var i = 0; i < saved.Count; i++)
        {
            var existing = ExcludedApps.FirstOrDefault(a => a.ExeName.Equals(saved[i], StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new AppItem(saved[i]);
                added.Add(existing);
                ExcludedApps.Insert(Math.Min(i, ExcludedApps.Count), existing);
            }
            else if (ExcludedApps.IndexOf(existing) != i)
            {
                ExcludedApps.Move(ExcludedApps.IndexOf(existing), i);
            }
        }
        HasExcludedApps = ExcludedApps.Count > 0;
        if (added.Count > 0) _ = DescribeAsync(added);
    }

    // Looks up names and icons off the UI thread: reading other processes can take a moment.
    private async Task DescribeAsync(IReadOnlyList<AppItem> items)
    {
        var catalog = _context.Services.Apps;
        var names = items.Select(i => i.ExeName).ToArray();
        IReadOnlyList<RunningApp?> found;
        try
        {
            found = await Task.Run(() => names.Select(catalog.Find).ToArray(), _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        for (var i = 0; i < items.Count; i++)
            if (found[i] is { } app) items[i].Describe(app);
    }

    private async Task LoadRunningAppsAsync()
    {
        IsLoadingRunningApps = true;
        try
        {
            var catalog = _context.Services.Apps;
            var apps = await Task.Run(catalog.GetRunningApps, _lifetime.Token);
            var settings = _context.Settings;
            var hidden = settings.ExcludedApps.Concat(_context.Services.ProtectedApps).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RunningApps.Clear();
            foreach (var app in apps.Where(a => !hidden.Contains(a.ExeName))) RunningApps.Add(app);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoadingRunningApps = false;
        }
    }
}

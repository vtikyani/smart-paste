using System.Text.Json;
using SmartPaste.App.Infrastructure;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-settings-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void MissingFileIsFirstRunWithSafeDefaults()
    {
        var store = new SettingsStore(PathName);
        Assert.True(store.IsFirstRun);
        Assert.True(store.Current.Enabled);
        Assert.False(store.Current.ReadScreenshots);
        Assert.Equal(AppSettings.DefaultParsingModel, store.Current.ParsingModel);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void UpdateSavesAndReloads()
    {
        var store = new SettingsStore(PathName);
        store.Update(s => s with { ProfileLifetimeMinutes = 30, ExcludedApps = ["Slack", "code.exe"] });
        var reloaded = new SettingsStore(PathName);
        Assert.False(reloaded.IsFirstRun);
        Assert.Equal(30, reloaded.Current.ProfileLifetimeMinutes);
        Assert.Equal(["code.exe", "Slack.exe"], reloaded.Current.ExcludedApps);
    }

    [Fact]
    public void UnchangedUpdateDoesNotRaiseChanged()
    {
        var store = new SettingsStore(PathName);
        store.Update(s => s with { ExcludedApps = ["a.exe"] });
        var raised = 0;
        store.Changed += (_, _) => raised++;
        store.Update(s => s with { ExcludedApps = ["a.exe"] });
        Assert.Equal(0, raised);
        store.Update(s => s with { Enabled = false });
        Assert.Equal(1, raised);
    }

    [Fact]
    public void InvalidValuesAreClamped()
    {
        var store = new SettingsStore(PathName);
        var saved = store.Update(s => s with { ProfileLifetimeMinutes = 7, ParsingModel = "  ", PickerHotkey = " " });
        Assert.Equal(15, saved.ProfileLifetimeMinutes);
        Assert.Equal(AppSettings.DefaultParsingModel, saved.ParsingModel);
        Assert.Null(saved.PickerHotkey);
    }

    [Fact]
    public void VersionOneFileMigrates()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, "{\"Enabled\":false,\"ParseScreenshots\":true,\"StartAtLogin\":true}");
        var store = new SettingsStore(PathName);
        Assert.False(store.IsFirstRun);
        Assert.False(store.Current.Enabled);
        Assert.True(store.Current.ReadScreenshots);
        Assert.True(store.Current.StartAtLogin);
        Assert.True(store.Current.OnboardingCompleted);
        Assert.Equal(AppSettings.CurrentVersion, store.Current.Version);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathName, "{ not json");
        var store = new SettingsStore(PathName);
        Assert.True(store.Current.Enabled);
    }

    [Fact]
    public void SavedFileUsesReadableNames()
    {
        var store = new SettingsStore(PathName);
        store.Update(s => s with { Theme = ThemePreference.Dark });
        using var doc = JsonDocument.Parse(File.ReadAllText(PathName));
        Assert.Equal("dark", doc.RootElement.GetProperty("theme").GetString());
        Assert.Equal(AppSettings.CurrentVersion, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void PauseIsReportedUntilItEnds()
    {
        var now = DateTimeOffset.Now;
        var settings = new AppSettings { PausedUntil = now.AddMinutes(5) };
        Assert.False(settings.IsActiveAt(now));
        Assert.True(settings.IsActiveAt(now.AddMinutes(6)));
    }
}

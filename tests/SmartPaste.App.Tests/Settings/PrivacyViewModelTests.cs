using SmartPaste.App.Infrastructure;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Settings.Pages;

namespace SmartPaste.App.Tests.Settings;

public sealed class PrivacyViewModelTests
{
    [Fact]
    public void AddingByNameSavesTheProgramName() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new PrivacyViewModel(ui.Context);
        Assert.False(vm.HasExcludedApps);
        Assert.False(vm.AddTypedAppCommand.CanExecute(null));

        vm.NewAppName = @"C:\Program Files\Slack\slack.exe";
        vm.AddTypedAppCommand.Execute(null);

        Assert.Equal(["slack.exe"], ui.Settings.Current.ExcludedApps);
        Assert.Equal(["slack.exe"], vm.ExcludedApps.Select(a => a.ExeName));
        Assert.True(vm.HasExcludedApps);
        Assert.Equal("", vm.NewAppName);
        Assert.Equal("", vm.AddError);
    });

    [Fact]
    public void ProblemsAreExplainedAndNothingIsSaved() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi(protectedApps: ["keepass.exe"]);
        ui.Settings.Update(s => s with { ExcludedApps = ["code.exe"] });
        using var vm = new PrivacyViewModel(ui.Context);

        vm.NewAppName = "Code";
        vm.AddTypedAppCommand.Execute(null);
        Assert.Equal("Code.exe is already on your list.", vm.AddError);

        vm.NewAppName = "KeePass";
        Assert.Equal("", vm.AddError);
        vm.AddTypedAppCommand.Execute(null);
        Assert.Equal("Smart Paste already leaves KeePass.exe alone.", vm.AddError);

        Assert.Equal(["code.exe"], ui.Settings.Current.ExcludedApps);
    });

    [Fact]
    public void RunningAppsCanBeAddedAndRemoved() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi(protectedApps: ["keepass.exe"]);
        ui.Apps.Running.Add(new RunningApp("slack.exe", "Slack", null));
        ui.Apps.Running.Add(new RunningApp("keepass.exe", "KeePass", null));
        ui.Apps.Running.Add(new RunningApp("Code.exe", "Visual Studio Code", null));
        ui.Settings.Update(s => s with { ExcludedApps = ["code.exe"] });
        using var vm = new PrivacyViewModel(ui.Context);

        vm.IsAddingApp = true;
        await UiTestHost.Settle(50);
        // Protected and already excluded apps aren't offered.
        var slack = Assert.Single(vm.RunningApps);
        Assert.Equal("Slack", slack.DisplayName);

        vm.AddRunningAppCommand.Execute(slack);
        Assert.Empty(vm.RunningApps);
        Assert.Equal(["code.exe", "slack.exe"], ui.Settings.Current.ExcludedApps);

        vm.RemoveAppCommand.Execute(vm.ExcludedApps.Single(a => a.ExeName == "code.exe"));
        Assert.Equal(["slack.exe"], ui.Settings.Current.ExcludedApps);
        Assert.Equal(["slack.exe"], vm.ExcludedApps.Select(a => a.ExeName));
    });

    [Fact]
    public void RunningAppsShowTheirRealNames() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi(protectedApps: ["1password.exe"]);
        ui.Apps.Running.Add(new RunningApp("Code.exe", "Visual Studio Code", null));
        ui.Apps.Running.Add(new RunningApp("1Password.exe", "1Password", null));
        ui.Settings.Update(s => s with { ExcludedApps = ["code.exe", "notepad.exe"] });
        using var vm = new PrivacyViewModel(ui.Context);
        await UiTestHost.Settle(50);

        Assert.Equal(["Visual Studio Code", "notepad.exe"], vm.ExcludedApps.Select(a => a.Title));
        Assert.Equal(["code.exe", ""], vm.ExcludedApps.Select(a => a.Detail));
        Assert.Equal("1Password", Assert.Single(vm.ProtectedApps).DisplayName);
    });

    [Fact]
    public void ScreenshotsAndLifetimeComeFromSettings() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new PrivacyViewModel(ui.Context);
        Assert.False(vm.ReadScreenshots);
        Assert.Equal("15 minutes", vm.BlockLifetime);

        vm.ReadScreenshots = true;
        Assert.True(ui.Settings.Current.ReadScreenshots);

        ui.Settings.Update(s => s with { ProfileLifetimeMinutes = 60 });
        await UiTestHost.Settle();
        Assert.Equal("1 hour", vm.BlockLifetime);
    });

    [Fact]
    public void OpensTheLogFolder() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new PrivacyViewModel(ui.Context);

        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal([Log.Directory], ui.Shell.Opened);
    });
}

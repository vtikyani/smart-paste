using SmartPaste.App.Infrastructure;
using SmartPaste.App.UI.Settings.Pages;

namespace SmartPaste.App.Tests.Settings;

public sealed class AboutViewModelTests
{
    [Fact]
    public void AnUninstalledCopySaysItCanNotUpdate() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Updates.IsSupported = false;
        using var vm = new AboutViewModel(ui.Context);

        Assert.Equal(UpdateStage.Unsupported, vm.Stage);
        Assert.Equal("This copy wasn't installed with the installer, so it can't update itself.", vm.UpdateMessage);
        Assert.False(vm.CheckCommand.CanExecute(null));
        Assert.Equal(AppInfo.Version, vm.Version);
    });

    [Fact]
    public void CheckingFindsNothingNew() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new AboutViewModel(ui.Context);
        Assert.Equal(UpdateStage.Idle, vm.Stage);
        Assert.Equal("", vm.UpdateMessage);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(UpdateStage.UpToDate, vm.Stage);
        Assert.StartsWith("You're up to date. Checked at ", vm.UpdateMessage);
        Assert.Equal("Check again", vm.CheckText);
    });

    [Fact]
    public void AFoundUpdateIsDownloadedAndCanRestartNow() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var update = new FakeUpdate("9.1.0");
        ui.Updates.Available = update;
        using var vm = new AboutViewModel(ui.Context);

        await vm.CheckCommand.ExecuteAsync(null);
        await UiTestHost.Settle();
        Assert.Equal(100, vm.DownloadPercent);
        Assert.Equal(UpdateStage.Ready, vm.Stage);
        Assert.Equal("Version 9.1.0 is ready. It installs the next time Smart Paste starts.", vm.UpdateMessage);
        Assert.Same(update, ui.State.Update);
        Assert.Null(ui.Updates.Applied);
        Assert.True(vm.RestartCommand.CanExecute(null));

        await vm.RestartCommand.ExecuteAsync(null);
        Assert.Same(update, ui.Updates.Applied);
        Assert.Equal(UpdateStage.Restarting, vm.Stage);
    });

    [Fact]
    public void AFailedDownloadIsNotOfferedAsReady() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Updates.Available = new FakeUpdate("9.1.0");
        ui.Updates.FailDownloads = true;
        using var vm = new AboutViewModel(ui.Context);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(UpdateStage.Failed, vm.Stage);
        Assert.Equal("Couldn't download the update. Check your internet connection, then try again.", vm.UpdateMessage);
        Assert.Null(ui.State.Update);
        Assert.True(vm.CheckCommand.CanExecute(null));
    });

    [Fact]
    public void AnUpdateFoundEarlierIsShownRightAway() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.State.Update = new FakeUpdate("9.2.0");
        using var vm = new AboutViewModel(ui.Context);

        Assert.Equal(UpdateStage.Ready, vm.Stage);
        Assert.False(vm.CheckCommand.CanExecute(null));
        Assert.True(vm.RestartCommand.CanExecute(null));
        Assert.Equal("", vm.ReleaseNotes);
    });

    [Fact]
    public void AnUpdateFoundWhileThePageIsOpenShowsUpWithItsNotes() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new AboutViewModel(ui.Context);
        Assert.Equal(UpdateStage.Idle, vm.Stage);

        // The automatic check found and downloaded a new version.
        ui.State.Update = new FakeUpdate("9.3.0", "## What's new\n\n- **Faster** pastes in [Edge](https://example.com)\n- Fewer clicks");

        Assert.Equal(UpdateStage.Ready, vm.Stage);
        Assert.Equal("Version 9.3.0 is ready. It installs the next time Smart Paste starts.", vm.UpdateMessage);
        Assert.Equal("What's new\n\n• Faster pastes in Edge\n• Fewer clicks", vm.ReleaseNotes);
    });

    [Fact]
    public void AClosedPageStopsFollowingUpdates() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        var vm = new AboutViewModel(ui.Context);
        vm.Dispose();

        ui.State.Update = new FakeUpdate("9.4.0");

        Assert.Equal(UpdateStage.Idle, vm.Stage);
    });

    [Fact]
    public void AFailedCheckCanBeRetried() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Updates.FailChecks = true;
        using var vm = new AboutViewModel(ui.Context);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(UpdateStage.Failed, vm.Stage);
        Assert.Equal("Couldn't check for updates. Check your internet connection, then try again.", vm.UpdateMessage);
        Assert.Equal("Try again", vm.CheckText);
        Assert.True(vm.CheckCommand.CanExecute(null));
    });

    [Fact]
    public void AutomaticChecksAreASetting() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new AboutViewModel(ui.Context);
        Assert.True(vm.CheckForUpdates);

        vm.CheckForUpdates = false;
        Assert.False(ui.Settings.Current.CheckForUpdates);
    });

    [Fact]
    public void LinksAndTheTourOpen() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new AboutViewModel(ui.Context);
        Assert.True(vm.HasRepository);

        vm.OpenSourceCommand.Execute(null);
        vm.OpenPrivacyCommand.Execute(null);
        vm.OpenLicensesCommand.Execute(null);
        vm.ReportProblemCommand.Execute(null);
        vm.ShowWelcomeTourCommand.Execute(null);

        var repo = AppInfo.RepositoryUrl;
        Assert.Equal([repo, repo + "/blob/main/PRIVACY.md", repo + "/blob/main/THIRD-PARTY-NOTICES.md", repo + "/issues"], ui.Shell.Opened);
        Assert.StartsWith("https://", repo);
        Assert.Equal(1, ui.Navigator.OnboardingRequests);
    });
}

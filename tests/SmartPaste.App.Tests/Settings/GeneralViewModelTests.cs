using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings;
using SmartPaste.App.UI.Settings.Pages;

namespace SmartPaste.App.Tests.Settings;

public sealed class GeneralViewModelTests
{
    [Fact]
    public void ShowsTheSavedSettings() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Settings.Update(s => s with
        {
            Theme = ThemePreference.Dark,
            ProfileLifetimeMinutes = 30,
            ShowFieldHints = false,
            NotifyWhenArmed = true,
            StartAtLogin = false
        });
        using var vm = new GeneralViewModel(ui.Context);

        Assert.True(vm.IsEnabled);
        Assert.Equal(ThemePreference.Dark, vm.SelectedTheme?.Value);
        Assert.Equal(30, vm.SelectedLifetime?.Minutes);
        Assert.False(vm.ShowFieldHints);
        Assert.True(vm.ShowPasteConfirmation);
        Assert.True(vm.NotifyWhenArmed);
        // Tests run as an isolated copy, which never registers itself, so the saved setting is what shows.
        Assert.False(vm.StartAtLogin);
        Assert.False(vm.IsPaused);
    });

    [Fact]
    public void LifetimeChoicesMatchTheSettings() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new GeneralViewModel(ui.Context);
        Assert.Equal(AppSettings.LifetimeChoices, vm.Lifetimes.Select(l => l.Minutes));
        Assert.Equal(["5 minutes", "15 minutes", "30 minutes", "1 hour"], vm.Lifetimes.Select(l => l.Label));

        vm.SelectedLifetime = vm.Lifetimes.Single(l => l.Minutes == 60);
        Assert.Equal(60, ui.Settings.Current.ProfileLifetimeMinutes);
    });

    [Fact]
    public void ChangesAreSaved() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new GeneralViewModel(ui.Context);

        vm.SelectedTheme = vm.Themes.Single(t => t.Value == ThemePreference.Light);
        vm.ShowFieldHints = false;
        vm.ShowPasteConfirmation = false;
        vm.NotifyWhenArmed = true;
        vm.StartAtLogin = false;

        var saved = ui.Settings.Current;
        Assert.Equal(ThemePreference.Light, saved.Theme);
        Assert.False(saved.ShowFieldHints);
        Assert.False(saved.ShowPasteConfirmation);
        Assert.True(saved.NotifyWhenArmed);
        Assert.False(saved.StartAtLogin);
    });

    [Fact]
    public void TurningOffGoesThroughTheController() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new GeneralViewModel(ui.Context);

        vm.IsEnabled = false;
        await UiTestHost.Settle();

        Assert.False(ui.Settings.Current.Enabled);
        Assert.False(ui.State.IsEnabled);
        Assert.Equal("Ctrl+V pastes normally, and Smart Paste doesn't look at what you copy.", vm.EnabledDescription);
    });

    [Fact]
    public void APauseShowsUntilWhenAndResumeEndsIt() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new GeneralViewModel(ui.Context);

        ui.Controller.Pause(TimeSpan.FromMinutes(30));
        await UiTestHost.Settle();
        Assert.True(vm.IsPaused);
        Assert.StartsWith("Paused until ", vm.PauseText);
        Assert.EndsWith("Ctrl+V pastes normally until then.", vm.EnabledDescription);

        vm.ResumeCommand.Execute(null);
        await UiTestHost.Settle();
        Assert.False(vm.IsPaused);
        Assert.Null(ui.Settings.Current.PausedUntil);
        Assert.Equal("Ctrl+V pastes the right value into each form field.", vm.EnabledDescription);
    });

    [Fact]
    public void FollowsChangesMadeElsewhere() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new GeneralViewModel(ui.Context);

        // The tray turns Smart Paste off and another page changes the lifetime.
        ui.Controller.SetEnabled(false);
        ui.Settings.Update(s => s with { ProfileLifetimeMinutes = 5 });
        await UiTestHost.Settle();

        Assert.False(vm.IsEnabled);
        Assert.Equal(5, vm.SelectedLifetime?.Minutes);
    });

    [Fact]
    public void StatusCardFollowsTheApp() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new SettingsViewModel(ui.Context);
        string? requested = null;
        vm.PageRequested += page => requested = page;

        Assert.Equal(StatusTone.Ready, vm.StatusTone);
        Assert.Equal("Smart Paste is on", vm.StatusTitle);

        ui.State.JevStatus = ServiceStatus.KeyRejected;
        Assert.Equal(StatusTone.Attention, vm.StatusTone);
        vm.OpenStatusPageCommand.Execute(null);
        Assert.Equal(SettingsPages.Jev, requested);

        ui.State.JevStatus = ServiceStatus.Ready;
        ui.Controller.SetEnabled(false);
        await UiTestHost.Settle();
        Assert.Equal(StatusTone.Off, vm.StatusTone);
        Assert.Equal("Smart Paste is off", vm.StatusTitle);
        vm.OpenStatusPageCommand.Execute(null);
        Assert.Equal(SettingsPages.General, requested);
    });
}

using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.Tests.Settings;
using SmartPaste.App.UI.Onboarding;

namespace SmartPaste.App.Tests.Onboarding;

public sealed class OnboardingViewModelTests
{
    [Fact]
    public void WalksForwardAndBack() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);

        Assert.Equal(OnboardingStep.Welcome, vm.Step);
        Assert.Equal("Get started", vm.NextText);
        Assert.Equal("Step 1 of 6", vm.StepLabel);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.True(vm.CanSkip);

        vm.NextCommand.Execute(null);
        Assert.Equal(OnboardingStep.HowItWorks, vm.Step);
        Assert.True(vm.MovedForward);
        Assert.Equal("Next", vm.NextText);
        Assert.True(vm.BackCommand.CanExecute(null));

        vm.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Welcome, vm.Step);
        Assert.False(vm.MovedForward);
    });

    [Fact]
    public void DotsShowWhereYouAre() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);
        vm.Step = OnboardingStep.Privacy;

        Assert.Equal(OnboardingViewModel.StepCount, vm.Dots.Count);
        Assert.Equal([true, true, true, false, false, false], vm.Dots.Select(d => d.IsDone));
        Assert.Equal(OnboardingStep.Privacy, Assert.Single(vm.Dots, d => d.IsCurrent).Step);
    });

    [Fact]
    public void ConnectOffersToSkipUntilAKeyIsSaved() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);
        vm.Step = OnboardingStep.Connect;
        Assert.Equal("Skip for now", vm.NextText);

        vm.OpenRouterKey.KeyText = "sk-or-key";
        await vm.OpenRouterKey.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Next", vm.NextText);
        Assert.True(ui.Keys.Exists(CredentialStore.OpenRouter));
    });

    [Fact]
    public void FinishingMarksTheTourSeenAndAppliesStartAtLogin() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        vm.Step = OnboardingStep.Ready;
        Assert.Equal("Finish", vm.NextText);
        Assert.False(vm.CanSkip);
        Assert.True(vm.StartAtLogin);

        vm.StartAtLogin = false;
        vm.NextCommand.Execute(null);

        Assert.True(ui.Settings.Current.OnboardingCompleted);
        Assert.False(ui.Settings.Current.StartAtLogin);
        Assert.True(vm.IsCompleted);
        Assert.Equal(1, closed);
    });

    [Fact]
    public void SkippingMarksTheTourSeenButLeavesStartUpAlone() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Settings.Update(s => s with { StartAtLogin = true });
        using var vm = new OnboardingViewModel(ui.Context);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        vm.StartAtLogin = false;

        vm.SkipCommand.Execute(null);

        Assert.True(ui.Settings.Current.OnboardingCompleted);
        Assert.True(ui.Settings.Current.StartAtLogin);
        Assert.Equal(1, closed);
    });

    [Fact]
    public void CompletingTwiceSavesOnce() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);
        var saves = 0;
        ui.Settings.Changed += (_, _) => saves++;

        vm.Complete(finished: false);
        vm.Complete(finished: true);

        Assert.Equal(1, saves);
        Assert.True(ui.Settings.Current.OnboardingCompleted);
    });

    [Fact]
    public void PrivacyLinkOpensTheSettingsPage() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);

        vm.OpenPrivacySettingsCommand.Execute(null);

        Assert.Equal([SettingsPages.Privacy], ui.Navigator.SettingsRequests);
    });

    [Fact]
    public void PrivacyStepUsesTheRealLifetime() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new OnboardingViewModel(ui.Context);
        Assert.Equal("15 minutes", vm.BlockLifetime);

        ui.Settings.Update(s => s with { ProfileLifetimeMinutes = 30 });
        await UiTestHost.Settle();
        Assert.Equal("30 minutes", vm.BlockLifetime);
    });
}

using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings.Pages;
using Wpf.Ui.Controls;

namespace SmartPaste.App.Tests.Settings;

public sealed class JevViewModelTests
{
    [Fact]
    public void RouteChoiceIsSavedAndExplained() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.OpenRouter, "sk-or");
        using var vm = new JevViewModel(ui.Context);

        Assert.Equal(JevRoute.Auto, vm.SelectedRoute?.Value);
        Assert.Equal(["Automatic", "TypeSafe", "OpenRouter"], vm.Routes.Select(r => r.Label));
        Assert.Equal("Right now that's OpenRouter.", vm.RouteSummary);

        vm.SelectedRoute = vm.Routes.Single(r => r.Value == JevRoute.TypeSafe);
        Assert.Equal(JevRoute.TypeSafe, ui.Settings.Current.JevRoute);
        Assert.StartsWith("No TypeSafe key is saved", vm.RouteSummary);
    });

    [Fact]
    public void SavingAKeyUpdatesTheRouteSummary() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new JevViewModel(ui.Context);
        Assert.Equal("No key is saved yet, so built-in labels pick values for now.", vm.RouteSummary);

        vm.TypeSafeKey.KeyText = "ts-key";
        await vm.TypeSafeKey.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Right now that's TypeSafe.", vm.RouteSummary);
    });

    [Fact]
    public void AskAheadIsSaved() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new JevViewModel(ui.Context);
        Assert.True(vm.AskJevAhead);

        vm.AskJevAhead = false;
        Assert.False(ui.Settings.Current.AskJevAhead);
    });

    [Fact]
    public void AlertsFollowTheLiveStatus() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new JevViewModel(ui.Context);
        Assert.Empty(vm.Alerts);

        ui.State.ParsingStatus = ServiceStatus.OutOfCredits;
        ui.State.JevStatus = ServiceStatus.OutOfCredits;
        var shared = Assert.Single(vm.Alerts);
        Assert.Equal("Your OpenRouter account is out of credits", shared.Title);

        ui.State.ParsingStatus = ServiceStatus.Ready;
        ui.State.JevStatus = ServiceStatus.Ready;
        Assert.Empty(vm.Alerts);
    });

    [Fact]
    public void AlertsNameTheServiceJevActuallyUses() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.TypeSafe, "ts-key");
        using var vm = new JevViewModel(ui.Context);

        ui.State.JevStatus = ServiceStatus.KeyRejected;
        var alert = Assert.Single(vm.Alerts);
        Assert.Equal(InfoBarSeverity.Error, alert.Severity);
        Assert.Equal("TypeSafe didn't accept your key", alert.Title);
    });

    [Fact]
    public void ParsingModelIsCheckedBeforeSaving() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new JevViewModel(ui.Context);
        Assert.Equal(AppSettings.DefaultParsingModel, vm.ParsingModel);
        Assert.True(vm.IsDefaultParsingModel);
        Assert.False(vm.ResetParsingModelCommand.CanExecute(null));

        vm.ParsingModel = "not a model";
        Assert.StartsWith("Model ids look like vendor/model", vm.ParsingModelError);
        Assert.Equal(AppSettings.DefaultParsingModel, ui.Settings.Current.ParsingModel);

        vm.ParsingModel = " openai/gpt-5 ";
        Assert.Equal("", vm.ParsingModelError);
        Assert.Equal("openai/gpt-5", ui.Settings.Current.ParsingModel);
        Assert.True(vm.ResetParsingModelCommand.CanExecute(null));

        vm.ResetParsingModelCommand.Execute(null);
        Assert.Equal(AppSettings.DefaultParsingModel, ui.Settings.Current.ParsingModel);
        Assert.Equal(AppSettings.DefaultParsingModel, vm.ParsingModel);
    });

    [Fact]
    public void ClearingTheModelGoesBackToTheDefault() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Settings.Update(s => s with { ParsingModel = "openai/gpt-5" });
        using var vm = new JevViewModel(ui.Context);

        vm.ParsingModel = "   ";
        Assert.Equal(AppSettings.DefaultParsingModel, ui.Settings.Current.ParsingModel);
        Assert.Equal(AppSettings.DefaultParsingModel, vm.ParsingModel);
    });
}

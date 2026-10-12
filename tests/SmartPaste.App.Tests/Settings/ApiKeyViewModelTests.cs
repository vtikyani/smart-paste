using System.Net.Http;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings;

namespace SmartPaste.App.Tests.Settings;

public sealed class ApiKeyViewModelTests
{
    [Fact]
    public void StartsFromWhatIsSaved() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.TypeSafe, "ts-key");
        using var openRouter = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);
        using var typeSafe = new ApiKeyViewModel(ApiKeyService.TypeSafe, ui.Context);

        Assert.False(openRouter.IsSaved);
        Assert.Equal("Not set", openRouter.StateText);
        Assert.Equal("https://openrouter.ai/keys", openRouter.GetKeyUrl);
        Assert.True(typeSafe.IsSaved);
        Assert.Equal("Saved", typeSafe.StateText);
        Assert.True(typeSafe.IsOptional);
        Assert.Equal("https://typesafe.ai", typeSafe.GetKeyUrl);

        typeSafe.RemoveCommand.Execute(null);
        Assert.Equal("Optional", typeSafe.StateText);
    });

    [Fact]
    public void SaveStoresTheKeyAndChecksIt() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Connections.Result = new ConnectionCheck(true, "Connected. Jev is ready.");
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.KeyText = "  sk-or-new  ";
        Assert.True(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("sk-or-new", ui.Keys.Get(CredentialStore.OpenRouter));
        Assert.True(vm.IsSaved);
        Assert.Equal("", vm.KeyText);
        Assert.Equal(["openrouter:sk-or-new"], ui.Connections.Checked);
        Assert.Equal(KeyCheckState.Passed, vm.CheckState);
        Assert.Equal("Connected. Jev is ready.", vm.CheckMessage);
    });

    [Fact]
    public void ARejectedKeySaysSo() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Connections.Result = new ConnectionCheck(false, "");
        using var vm = new ApiKeyViewModel(ApiKeyService.TypeSafe, ui.Context);

        vm.KeyText = "ts-bad";
        await vm.TestCommand.ExecuteAsync(null);

        Assert.Equal(["typesafe:ts-bad"], ui.Connections.Checked);
        Assert.Equal(KeyCheckState.Failed, vm.CheckState);
        Assert.Equal("TypeSafe didn't accept the key.", vm.CheckMessage);
        // Testing doesn't save.
        Assert.False(ui.Keys.Exists(CredentialStore.TypeSafe));
    });

    [Fact]
    public void TestChecksTheSavedKeyWhenTheBoxIsEmpty() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.OpenRouter, "sk-or-saved");
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        Assert.True(vm.TestCommand.CanExecute(null));
        await vm.TestCommand.ExecuteAsync(null);
        Assert.Equal(["openrouter:sk-or-saved"], ui.Connections.Checked);
    });

    [Fact]
    public void ShowsProgressWhileChecking() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Connections.Gate = new TaskCompletionSource();
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        vm.KeyText = "sk-or-slow";
        var test = vm.TestCommand.ExecuteAsync(null);
        Assert.True(vm.IsChecking);
        Assert.Equal("Checking with OpenRouter…", vm.CheckMessage);
        Assert.False(vm.SaveCommand.CanExecute(null));

        ui.Connections.Gate.SetResult();
        await test;
        Assert.False(vm.IsChecking);
        Assert.Equal(KeyCheckState.Passed, vm.CheckState);
    });

    [Fact]
    public void ACheckThatThrowsIsReportedPlainly() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Connections.Gate = new TaskCompletionSource();
        ui.Connections.Gate.SetException(new HttpRequestException("network down"));
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        vm.KeyText = "sk-or-x";
        await vm.TestCommand.ExecuteAsync(null);

        Assert.Equal(KeyCheckState.Failed, vm.CheckState);
        Assert.Equal("Couldn't reach OpenRouter. Check your internet connection, then select Test.", vm.CheckMessage);
    });

    [Fact]
    public void WindowsRefusingTheKeyIsReported() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Keys.FailWrites = true;
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        vm.KeyText = "sk-or-x";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsSaved);
        Assert.Equal(KeyCheckState.Failed, vm.CheckState);
        Assert.Equal("Windows couldn't save the key. Try again in a moment.", vm.CheckMessage);
        Assert.Empty(ui.Connections.Checked);
    });

    [Fact]
    public void RemoveAsksFirst() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.OpenRouter, "sk-or-saved");
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        ui.Prompts.Answer = false;
        await vm.RemoveCommand.ExecuteAsync(null);
        Assert.True(ui.Keys.Exists(CredentialStore.OpenRouter));
        var question = Assert.Single(ui.Prompts.Asked);
        Assert.Equal("Remove your OpenRouter key?", question.Title);
        Assert.Equal("Remove key", question.ConfirmText);
        Assert.Contains("built-in labels will pick values", question.Message);

        ui.Prompts.Answer = true;
        await vm.RemoveCommand.ExecuteAsync(null);
        Assert.False(ui.Keys.Exists(CredentialStore.OpenRouter));
        Assert.False(vm.IsSaved);
        Assert.False(vm.RemoveCommand.CanExecute(null));
    });

    [Fact]
    public void RemovingOneKeyMentionsTheOther() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Keys.Set(CredentialStore.OpenRouter, "sk-or-saved");
        ui.Keys.Set(CredentialStore.TypeSafe, "ts-saved");
        using var typeSafe = new ApiKeyViewModel(ApiKeyService.TypeSafe, ui.Context);

        await typeSafe.RemoveCommand.ExecuteAsync(null);
        Assert.Equal("Jev will run through your OpenRouter key instead.", Assert.Single(ui.Prompts.Asked).Message);
    });

    [Fact]
    public void FollowsKeysSavedElsewhere() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new ApiKeyViewModel(ApiKeyService.OpenRouter, ui.Context);

        // The welcome tour saves a key while the settings window is open, from a background thread.
        await Task.Run(() => ui.Keys.Set(CredentialStore.OpenRouter, "sk-or-from-tour"));
        await UiTestHost.Settle();

        Assert.True(vm.IsSaved);
    });
}

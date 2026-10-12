using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;
using SmartPaste.App.UI.Tray;

// What the flyout shows and does, against the real AppState, AppController, and settings.
public sealed class FlyoutViewModelTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-flyout-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private void With(Action<AppState, AppController, FlyoutViewModel, RecordingNavigator> test) => WpfHost.Run(() =>
    {
        var settings = new SettingsStore(_settingsPath);
        var state = new AppState();
        using var controller = new AppController(state, settings, Dispatcher.CurrentDispatcher);
        state.JevStatus = ServiceStatus.Ready;
        var navigator = new RecordingNavigator();
        using var model = new FlyoutViewModel(state, controller, settings, navigator, new FixedClock(DateTimeOffset.Now));
        test(state, controller, model, navigator);
    });

    [Fact]
    public void MasterSwitchTurnsSmartPasteOffAndBackOn() => With((state, _, model, _) =>
    {
        Assert.True(model.IsOn);
        Assert.True(model.IsWaiting);
        model.IsOn = false;
        Assert.False(state.IsEnabled);
        Assert.True(model.IsOff);
        Assert.False(model.CanPause);
        model.TurnOnCommand.Execute(null);
        Assert.True(state.IsEnabled);
        Assert.True(model.IsOn);
    });

    [Fact]
    public void PauseAndResume() => With((state, _, model, _) =>
    {
        Assert.Equal(["15 minutes", "1 hour", "Until tomorrow"], model.PauseOptions.Select(o => o.Label));
        model.PauseOptions[0].Command.Execute(null);
        Assert.NotNull(state.PausedUntil);
        Assert.True(model.IsPaused);
        Assert.StartsWith("Paused until ", model.StatusLine, StringComparison.Ordinal);
        model.ResumeCommand.Execute(null);
        Assert.Null(state.PausedUntil);
        Assert.True(model.IsWaiting);
        Assert.Equal("Ready for your next copy", model.StatusLine);
    });

    [Fact]
    public void LinksOpenTheRightSettingsPageAndCloseTheFlyout() => With((_, _, model, navigator) =>
    {
        var closes = 0;
        model.CloseRequested += (_, _) => closes++;
        model.OpenServiceSettingsCommand.Execute(null);
        model.OpenUpdateCommand.Execute(null);
        model.OpenSettingsCommand.Execute(null);
        Assert.Equal([SettingsPages.Jev, SettingsPages.About, null], navigator.SettingsPagesOpened);
        Assert.Equal(3, closes);
    });

    [Fact]
    public void ValuesStartHiddenEveryTimeTheFlyoutOpens() => With((state, _, model, _) =>
    {
        state.Armed = DocSample.Armed(DateTimeOffset.Now);
        model.Opened();
        Assert.True(model.IsArmed);
        var first = model.Armed!.Groups[0].Rows[0];
        Assert.Equal(DisplayText.Masked, first.DisplayValue);
        model.ToggleRevealCommand.Execute(null);
        Assert.Equal("Jordan", first.DisplayValue);
        model.Closed();
        model.Opened();
        Assert.False(model.RevealValues);
        Assert.Equal(DisplayText.Masked, model.Armed!.Groups[0].Rows[0].DisplayValue);
        model.Closed();
    });

    [Fact]
    public void ClearAsksTheBlocksOwnerToDropIt() => With((state, controller, model, _) =>
    {
        var asked = 0;
        controller.ClearRequested += (_, _) => asked++;
        state.Armed = DocSample.Armed(DateTimeOffset.Now);
        model.ClearCommand.Execute(null);
        Assert.Equal(1, asked);
    });

    [Fact]
    public void RecentShowsTheLatestFiveNewestFirst() => With((state, _, model, _) =>
    {
        Assert.False(model.HasRecent);
        var now = DateTimeOffset.Now;
        for (var i = 7; i >= 1; i--)
            state.AddActivity(new ActivityEntry(now.AddMinutes(-i), $"Field {i}", "Email", ActivityOutcome.Pasted, DecisionMaker.Jev, "chrome.exe"));
        Assert.True(model.HasRecent);
        Assert.Equal(["Field 1", "Field 2", "Field 3", "Field 4", "Field 5"], model.Recent.Select(r => r.Label));
        Assert.Equal("Email · Jev · chrome", model.Recent[0].Detail);
    });

    [Fact]
    public void RecentNeverShowsValues() => With((state, _, model, _) =>
    {
        state.AddActivity(new ActivityEntry(DateTimeOffset.Now, "Name", null, ActivityOutcome.PastedOriginal, null, "msedge.exe"));
        state.AddActivity(new ActivityEntry(DateTimeOffset.Now, "", "Phone", ActivityOutcome.Pasted, DecisionMaker.User, "notepad.exe"));
        Assert.Equal("Unlabeled field", model.Recent[0].Label);
        Assert.Equal("Phone · your choice · notepad", model.Recent[0].Detail);
        Assert.Equal("Pasted your clipboard as is · msedge", model.Recent[1].Detail);
    });

    [Fact]
    public void ARejectedKeyRaisesTheBanner() => With((state, _, model, _) =>
    {
        Assert.False(model.Services.IsProblem);
        state.JevStatus = ServiceStatus.KeyRejected;
        Assert.True(model.Services.IsProblem);
        Assert.Equal(Tone.Critical, model.Services.Tone);
        Assert.NotNull(model.Services.Detail);
    });
}

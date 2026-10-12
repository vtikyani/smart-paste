using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Tray;

// When settings.json can't be written (a full disk, a locked file), a change must still take effect everywhere and
// the user must hear that it may not last. Here a folder sits where the file should be, so every save fails.
public sealed class SettingsSaveFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-save-fail-" + Guid.NewGuid().ToString("N"));

    public SettingsSaveFailureTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "settings.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private SettingsStore Store() => new(Path.Combine(_dir, "settings.json"));

    private static void SaveFails(Action save)
    {
        var error = Record.Exception(save);
        Assert.True(error is IOException or UnauthorizedAccessException, error?.GetType().Name ?? "no error");
    }

    [Fact]
    public void AFailedSaveStillAppliesTheChangeAndRaisesChanged()
    {
        var store = Store();
        AppSettings? heard = null;
        store.Changed += (_, current) => heard = current;

        SaveFails(() => store.Update(s => s with { Enabled = false, ExcludedApps = ["slack.exe"] }));

        Assert.False(store.Current.Enabled);
        Assert.Equal(["slack.exe"], store.Current.ExcludedApps);
        Assert.NotNull(heard);
        Assert.False(heard!.Enabled);
    }

    [Fact]
    public void TrayAndFlyoutChangesStillApplyAndAreReported() => WpfHost.Run(() =>
    {
        var store = Store();
        var state = new AppState();
        using var controller = new AppController(state, store, Dispatcher.CurrentDispatcher);
        var reported = 0;
        controller.SaveFailed += (_, _) => reported++;

        controller.TryChange(c => c.SetEnabled(false), "tray");
        Assert.False(state.IsEnabled);
        Assert.Equal(1, reported);

        using var flyout = new FlyoutViewModel(state, controller, store, new RecordingNavigator(), new FixedClock(DateTimeOffset.Now));
        flyout.TurnOnCommand.Execute(null);
        Assert.True(state.IsEnabled);
        Assert.True(flyout.IsOn);
        flyout.PauseOptions[0].Command.Execute(null);
        Assert.True(flyout.IsPaused);
        Assert.Equal(3, reported);
    });

    [Fact]
    public void APauseStillEndsWhenTheSettingsCanNotBeSaved() => WpfHost.Run(() =>
    {
        var store = Store();
        SaveFails(() => store.Update(s => s with { PausedUntil = DateTimeOffset.Now.AddSeconds(-1) }));
        var state = new AppState();
        using var controller = new AppController(state, store, Dispatcher.CurrentDispatcher);
        var reported = 0;
        var changed = 0;
        controller.SaveFailed += (_, _) => reported++;
        store.Changed += (_, _) => changed++;

        controller.Tick();

        Assert.Null(store.Current.PausedUntil);
        Assert.True(store.Current.IsActiveAt(DateTimeOffset.Now));
        Assert.Equal(1, changed);
        Assert.Equal(1, reported);
        Assert.Equal(TrayStatus.Ready, state.Status);
    });
}

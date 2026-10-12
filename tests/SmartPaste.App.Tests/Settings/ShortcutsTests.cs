using System.Windows.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Controls;
using SmartPaste.App.UI.Settings.Pages;

namespace SmartPaste.App.Tests.Settings;

public sealed class ShortcutsTests
{
    [Theory]
    [InlineData("Win+Alt+V", true, "Working", "Ready. Works in any app.")]
    [InlineData("Win+Alt+V", false, "Problem", "Another app already uses this shortcut. Pick a different one.")]
    [InlineData(null, null, "Off", "Off. Select the box to set a shortcut.")]
    [InlineData("Win+Alt+V", null, "None", "")]
    public void StatusTextMatchesTheShortcutManager(string? hotkey, bool? registered, string health, string message)
    {
        var (actualHealth, actualMessage) = ShortcutSettingViewModel.Describe(hotkey, registered);
        Assert.Equal(health, actualHealth.ToString());
        Assert.Equal(message, actualMessage);
    }

    [Fact]
    public void ShowsAndSavesBothShortcuts() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Hotkeys.Registered[HotkeyNames.Picker] = true;
        using var vm = new ShortcutsViewModel(ui.Context);

        Assert.Equal(AppSettings.DefaultPickerHotkey, vm.Picker.Hotkey);
        Assert.Equal(ShortcutHealth.Working, vm.Picker.Health);
        Assert.Null(vm.Toggle.Hotkey);
        Assert.Equal(ShortcutHealth.Off, vm.Toggle.Health);
        Assert.False(vm.Picker.CanRestoreDefault);

        vm.Toggle.Hotkey = "Ctrl+Alt+S";
        vm.Picker.Hotkey = null;

        Assert.Equal("Ctrl+Alt+S", ui.Settings.Current.ToggleHotkey);
        Assert.Null(ui.Settings.Current.PickerHotkey);
        Assert.True(vm.Picker.CanRestoreDefault);

        vm.Picker.RestoreDefaultCommand.Execute(null);
        Assert.Equal(AppSettings.DefaultPickerHotkey, ui.Settings.Current.PickerHotkey);
    });

    [Fact]
    public void TheTwoShortcutsCanNotBeTheSame() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new ShortcutsViewModel(ui.Context);

        vm.Toggle.Hotkey = "Alt+Win+V";
        Assert.Equal(ShortcutHealth.Problem, vm.Toggle.Health);
        Assert.Equal("Those keys are already your picker shortcut. Pick a different shortcut.", vm.Toggle.Message);
        await UiTestHost.Settle();

        Assert.Null(ui.Settings.Current.ToggleHotkey);
        Assert.Null(vm.Toggle.Hotkey);
    });

    [Fact]
    public void FollowsTheShortcutManager() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new ShortcutsViewModel(ui.Context);
        Assert.Equal(ShortcutHealth.None, vm.Picker.Health);

        ui.Hotkeys.Registered[HotkeyNames.Picker] = false;
        await Task.Run(ui.Hotkeys.Raise);
        await UiTestHost.Settle();

        Assert.Equal(ShortcutHealth.Problem, vm.Picker.Health);
    });

    [Fact]
    public void RecorderProblemsShowUnderTheShortcut() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new ShortcutsViewModel(ui.Context);

        vm.Picker.RecorderProblem = "Ctrl+C copies in every app. Pick another shortcut.";
        Assert.Equal(ShortcutHealth.Problem, vm.Picker.Health);
        Assert.Equal("Ctrl+C copies in every app. Pick another shortcut.", vm.Picker.Message);

        vm.Picker.RecorderProblem = "";
        Assert.NotEqual(ShortcutHealth.Problem, vm.Picker.Health);
    });

    [Fact]
    public void RecorderTakesAShortcutWithCtrlAltOrWin() => UiTestHost.Run(() =>
    {
        var box = new HotkeyBox { Hotkey = "Win+Alt+V" };
        box.StartRecording();
        Assert.True(box.IsRecording);
        Assert.True(HotkeyBox.IsAnyRecording);

        // Holding modifiers alone keeps listening.
        Assert.True(box.Record(Key.LeftCtrl, ModifierKeys.Control));
        Assert.True(box.IsRecording);

        Assert.True(box.Record(Key.K, ModifierKeys.Control | ModifierKeys.Alt));
        Assert.False(box.IsRecording);
        Assert.False(HotkeyBox.IsAnyRecording);
        Assert.Equal("Ctrl+Alt+K", box.Hotkey);
    });

    [Fact]
    public void RecorderExplainsWhatItCanNotTake() => UiTestHost.Run(() =>
    {
        var box = new HotkeyBox { Hotkey = "Win+Alt+V" };
        box.StartRecording();

        Assert.True(box.Record(Key.K, ModifierKeys.Shift));
        Assert.True(box.IsRecording);
        Assert.Equal("Hold Ctrl, Alt, or the Windows key, then press another key.", box.Problem);

        Assert.True(box.Record(Key.C, ModifierKeys.Control));
        Assert.True(box.IsRecording);
        Assert.Equal("Ctrl+C copies in every app. Pick another shortcut.", box.Problem);
        Assert.Equal("Win+Alt+V", box.Hotkey);

        // Esc cancels and keeps the old shortcut.
        Assert.True(box.Record(Key.Escape, ModifierKeys.None));
        Assert.False(box.IsRecording);
        Assert.Equal("Win+Alt+V", box.Hotkey);
    });

    [Fact]
    public void RecorderLetsTabMoveOn() => UiTestHost.Run(() =>
    {
        var box = new HotkeyBox();
        box.StartRecording();
        Assert.False(box.Record(Key.Tab, ModifierKeys.None));
        Assert.False(box.Record(Key.Tab, ModifierKeys.Shift));
        box.CancelRecording();
        Assert.False(box.IsRecording);
    });
}

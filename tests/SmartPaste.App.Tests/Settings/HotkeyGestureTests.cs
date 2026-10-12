using System.Windows.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.UI.Controls;

namespace SmartPaste.App.Tests.Settings;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void DefaultPickerShortcutParses()
    {
        Assert.True(HotkeyGesture.TryParse(AppSettings.DefaultPickerHotkey, out var gesture));
        Assert.Equal(ModifierKeys.Windows | ModifierKeys.Alt, gesture.Modifiers);
        Assert.Equal(Key.V, gesture.Key);
        Assert.Equal("Win+Alt+V", gesture.ToString());
        Assert.Equal(["Win", "Alt", "V"], gesture.DisplayParts);
    }

    [Theory]
    [InlineData("alt+ctrl+k", "Ctrl+Alt+K")]
    [InlineData("Shift+Control+Windows+F9", "Win+Ctrl+Shift+F9")]
    [InlineData(" Ctrl + Alt + 7 ", "Ctrl+Alt+7")]
    [InlineData("Win+PageDown", "Win+PageDown")]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("Ctrl+Alt+Num4", "Ctrl+Alt+Num4")]
    [InlineData("Ctrl+Alt+OemPeriod", "Ctrl+Alt+OemPeriod")]
    public void ParsesAnyOrderAndWritesTheStoredForm(string text, string stored)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(stored, gesture.ToString());
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again));
        Assert.Equal(gesture, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("Shift+V")]
    [InlineData("Ctrl+Ctrl+V")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+65")]
    [InlineData("Ctrl++V")]
    [InlineData("Hyper+V")]
    public void RejectsWhatCanNotBeAShortcut(string? text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData(ModifierKeys.Control, Key.C)]
    [InlineData(ModifierKeys.Control, Key.V)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.V)]
    [InlineData(ModifierKeys.Alt, Key.F4)]
    [InlineData(ModifierKeys.Windows, Key.L)]
    public void ReservedShortcutsAreRefusedWithAReason(ModifierKeys modifiers, Key key)
    {
        var problem = new HotkeyGesture(modifiers, key).Problem();
        Assert.NotNull(problem);
        Assert.EndsWith("Pick another shortcut.", problem);
    }

    [Fact]
    public void AShortcutNeedsCtrlAltOrWin()
    {
        Assert.Equal("Hold Ctrl, Alt, or the Windows key, then press another key.", new HotkeyGesture(ModifierKeys.Shift, Key.K).Problem());
        Assert.Null(new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.K).Problem());
    }

    [Fact]
    public void KeycapsUseFriendlyNames()
    {
        Assert.Equal(["Ctrl", "Page Down"], new HotkeyGesture(ModifierKeys.Control, Key.PageDown).DisplayParts);
        Assert.Equal(["Alt", "Num 7"], new HotkeyGesture(ModifierKeys.Alt, Key.NumPad7).DisplayParts);
        Assert.Equal(["Win", "7"], new HotkeyGesture(ModifierKeys.Windows, Key.D7).DisplayParts);
    }

    [Fact]
    public void VirtualKeyMatchesWindows() =>
        Assert.Equal(0x56, new HotkeyGesture(ModifierKeys.Windows | ModifierKeys.Alt, Key.V).VirtualKey);
}

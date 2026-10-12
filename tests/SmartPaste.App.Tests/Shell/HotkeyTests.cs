using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Win+Alt+V", KeyModifiers.Windows | KeyModifiers.Alt, 0x56)]
    [InlineData("Ctrl+Shift+F9", KeyModifiers.Control | KeyModifiers.Shift, 0x78)]
    [InlineData("Ctrl+Alt+Space", KeyModifiers.Control | KeyModifiers.Alt, 0x20)]
    [InlineData("Win+Ctrl+Shift+B", KeyModifiers.Windows | KeyModifiers.Control | KeyModifiers.Shift, 0x42)]
    [InlineData("Alt+F24", KeyModifiers.Alt, 0x87)]
    [InlineData("Ctrl+Alt+NumpadAdd", KeyModifiers.Control | KeyModifiers.Alt, 0x6B)]
    [InlineData("Ctrl+Alt+=", KeyModifiers.Control | KeyModifiers.Alt, 0xBB)]
    [InlineData("Win+Alt+\\", KeyModifiers.Windows | KeyModifiers.Alt, 0xDC)]
    [InlineData("Ctrl+Alt+7", KeyModifiers.Control | KeyModifiers.Alt, 0x37)]
    public void ParsesAndRoundTrips(string text, KeyModifiers modifiers, int virtualKey)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey, out var error));
        Assert.Equal(HotkeyError.None, error);
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(virtualKey, hotkey.VirtualKey);
        Assert.Equal(text, hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Theory]
    [InlineData("alt+win+v", "Win+Alt+V")]
    [InlineData(" Control + Shift + f9 ", "Ctrl+Shift+F9")]
    [InlineData("Windows+Alt+Escape", "Win+Alt+Esc")]
    [InlineData("Ctrl+Alt+PgDn", "Ctrl+Alt+PageDown")]
    [InlineData("Ctrl+Alt+Return", "Ctrl+Alt+Enter")]
    public void AcceptsAnyOrderCaseAndAliasesAndWritesTheCanonicalForm(string text, string canonical)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(canonical, hotkey.ToString());
    }

    [Theory]
    [InlineData(null, nameof(HotkeyError.Empty))]
    [InlineData("", nameof(HotkeyError.Empty))]
    [InlineData("   ", nameof(HotkeyError.Empty))]
    [InlineData("Ctrl+", nameof(HotkeyError.Malformed))]
    [InlineData("+V", nameof(HotkeyError.Malformed))]
    [InlineData("Ctrl++V", nameof(HotkeyError.Malformed))]
    [InlineData("Ctrl+Ctrl+V", nameof(HotkeyError.Malformed))]
    [InlineData("Ctrl+Alt", nameof(HotkeyError.Malformed))]
    [InlineData("Hyper+V", nameof(HotkeyError.Malformed))]
    [InlineData("V+Ctrl", nameof(HotkeyError.Malformed))]
    [InlineData("Ctrl+Alt+Banana", nameof(HotkeyError.UnknownKey))]
    [InlineData("Ctrl+Alt+F25", nameof(HotkeyError.UnknownKey))]
    [InlineData("V", nameof(HotkeyError.MissingModifier))]
    [InlineData("F9", nameof(HotkeyError.MissingModifier))]
    [InlineData("Shift+A", nameof(HotkeyError.MissingModifier))]
    [InlineData("Ctrl+C", nameof(HotkeyError.Reserved))]
    [InlineData("Ctrl+V", nameof(HotkeyError.Reserved))]
    [InlineData("Ctrl+Shift+V", nameof(HotkeyError.Reserved))]
    [InlineData("Alt+Tab", nameof(HotkeyError.Reserved))]
    [InlineData("Alt+F4", nameof(HotkeyError.Reserved))]
    [InlineData("Ctrl+Alt+Delete", nameof(HotkeyError.Reserved))]
    [InlineData("Ctrl+Shift+Esc", nameof(HotkeyError.Reserved))]
    [InlineData("Win+L", nameof(HotkeyError.Reserved))]
    [InlineData("Win+V", nameof(HotkeyError.Reserved))]
    [InlineData("Win+.", nameof(HotkeyError.Reserved))]
    public void RejectsWhatCannotBeAShortcut(string? text, string expected)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey, out var error));
        Assert.Equal(expected, error.ToString());
        Assert.Equal(default(Hotkey), hotkey);
    }

    [Fact]
    public void TheDefaultPickerShortcutIsValid()
    {
        Assert.True(Hotkey.TryParse(AppSettings.DefaultPickerHotkey, out var hotkey));
        Assert.Equal(AppSettings.DefaultPickerHotkey, hotkey.ToString());
    }

    [Fact]
    public void ModifierAndLockKeysCannotBeTheKey()
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x14, 0x5B, 0x5C, 0x90, 0x91, 0xA0, 0xA2, 0xA4 })
        {
            Assert.False(Hotkey.TryCreate(KeyModifiers.Control | KeyModifiers.Alt, key, out _, out var error));
            Assert.Equal(HotkeyError.UnknownKey, error);
        }
    }

    [Fact]
    public void TryCreateBuildsWhatARecorderCaptured()
    {
        Assert.True(Hotkey.TryCreate(KeyModifiers.Windows | KeyModifiers.Alt, 'V', out var hotkey, out _));
        Assert.Equal("Win+Alt+V", hotkey.ToString());
    }

    [Fact]
    public void NativeModifiersMatchRegisterHotKey()
    {
        Assert.True(Hotkey.TryParse("Win+Ctrl+Alt+Shift+K", out var all));
        Assert.Equal(0xFu, all.NativeModifiers);
        Assert.True(Hotkey.TryParse("Alt+K", out var alt));
        Assert.Equal(0x1u, alt.NativeModifiers);
        Assert.True(Hotkey.TryParse("Ctrl+Alt+K", out var ctrlAlt));
        Assert.Equal(0x3u, ctrlAlt.NativeModifiers);
        Assert.True(Hotkey.TryParse("Win+Shift+K", out var winShift));
        Assert.Equal(0xCu, winShift.NativeModifiers);
    }
}

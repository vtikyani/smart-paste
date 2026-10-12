using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.Tests.Settings;

// Which global shortcuts are registered as Smart Paste pauses, resumes, and records new shortcuts. The registrations
// are recorded instead of made, so no real key combination is taken.
public sealed class ShortcutSyncTests
{
    private const string Toggle = "Ctrl+Alt+S";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly List<(string Name, string? Hotkey)> _calls = [];
    private readonly ShortcutSync _sync;

    public ShortcutSyncTests()
    {
        _sync = new ShortcutSync((name, hotkey) => _calls.Add((name, hotkey)));
    }

    private void Apply(AppSettings settings, DateTimeOffset at, bool recording = false) =>
        _sync.Apply(settings, settings.IsActiveAt(at), recording);

    [Fact]
    public void APauseThatRunsOutBringsThePickerShortcutBack()
    {
        var paused = new AppSettings { PausedUntil = Now.AddMinutes(15), ToggleHotkey = Toggle };
        Apply(paused, Now);
        Assert.Equal([(HotkeyNames.Picker, null), (HotkeyNames.Toggle, Toggle)], _calls);
        _calls.Clear();

        // What the pause clock saves once the time is up. Judged against the old settings at that moment, nothing
        // changed (both say "active"), which is how the shortcut used to stay off for good.
        var later = Now.AddMinutes(16);
        Apply(paused with { PausedUntil = null }, later);

        Assert.Equal([(HotkeyNames.Picker, AppSettings.DefaultPickerHotkey)], _calls);
    }

    [Fact]
    public void StartingDuringAPauseRegistersThePickerShortcutWhenItEnds()
    {
        var paused = new AppSettings { PausedUntil = Now.AddMinutes(5) };
        Apply(paused, Now);
        _calls.Clear();

        // The tick before the pause clock saves: same settings, but the pause is over.
        Apply(paused, Now.AddMinutes(6));

        Assert.Equal([(HotkeyNames.Picker, AppSettings.DefaultPickerHotkey)], _calls);
    }

    [Fact]
    public void TurningOffKeepsOnlyTheOnOffShortcut()
    {
        var on = new AppSettings { ToggleHotkey = Toggle };
        Apply(on, Now);
        _calls.Clear();

        Apply(on with { Enabled = false }, Now);

        Assert.Equal([(HotkeyNames.Picker, null)], _calls);
    }

    [Fact]
    public void RecordingSuspendsBothShortcutsAndRestoresThemFromTheSettings()
    {
        var settings = new AppSettings { ToggleHotkey = Toggle };
        Apply(settings, Now);
        _calls.Clear();

        Apply(settings, Now, recording: true);
        Assert.Equal([(HotkeyNames.Picker, null), (HotkeyNames.Toggle, null)], _calls);
        _calls.Clear();

        // The recorder saved a new picker shortcut, then stopped listening.
        Apply(settings with { PickerHotkey = "Ctrl+Alt+K" }, Now, recording: true);
        Assert.Empty(_calls);
        Apply(settings with { PickerHotkey = "Ctrl+Alt+K" }, Now);
        Assert.Equal([(HotkeyNames.Picker, "Ctrl+Alt+K"), (HotkeyNames.Toggle, Toggle)], _calls);
    }

    [Fact]
    public void NothingIsRegisteredAgainWhenNothingChanged()
    {
        var settings = new AppSettings { ToggleHotkey = Toggle };
        Apply(settings, Now);
        Apply(settings with { ExcludedApps = ["slack.exe"] }, Now);
        Apply(settings, Now.AddHours(1));

        Assert.Equal([(HotkeyNames.Picker, AppSettings.DefaultPickerHotkey), (HotkeyNames.Toggle, Toggle)], _calls);
    }
}

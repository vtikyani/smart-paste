using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App;

// Keeps the global shortcuts registered the way things are right now:
// - the picker shortcut only while Smart Paste is on, so it never takes a key combination while it is off;
// - the on/off shortcut always, so Smart Paste can be turned back on with it;
// - neither while a shortcut recorder is listening, so pressing a shortcut that is registered records it.
// It compares against what it registered last, never against the previous settings: a pause that runs out changes
// nothing in the settings that matters here, and the picker shortcut must still come back.
internal sealed class ShortcutSync(Action<string, string?> register)
{
    private readonly Dictionary<string, string?> _registered = new(StringComparer.Ordinal);

    // active: Smart Paste is on and not paused. recording: a shortcut recorder is listening.
    public void Apply(AppSettings settings, bool active, bool recording)
    {
        Set(HotkeyNames.Picker, active && !recording ? settings.PickerHotkey : null);
        Set(HotkeyNames.Toggle, recording ? null : settings.ToggleHotkey);
    }

    private void Set(string name, string? hotkey)
    {
        if (_registered.TryGetValue(name, out var current) && string.Equals(current, hotkey, StringComparison.Ordinal)) return;
        _registered[name] = hotkey;
        register(name, hotkey);
    }
}

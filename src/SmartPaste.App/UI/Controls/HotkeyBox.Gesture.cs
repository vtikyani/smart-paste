using System.Runtime.InteropServices;
using System.Windows.Input;
using SmartPaste.App.Services;
using SmartPaste.App.Shell;

namespace SmartPaste.App.UI.Controls;

// A global shortcut such as "Win+Alt+V", as the shortcut recorder sees it (WPF keys). Storage goes through the hotkey
// manager's own Hotkey type: ToString() is Hotkey's canonical text and TryParse reads only that, so whatever the
// recorder saves in AppSettings (PickerHotkey, ToggleHotkey) is exactly what the hotkey manager can register.
internal readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    private const ModifierKeys Required = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows;

    // Plain-English reasons for the shortcuts that would break typing or Windows itself in every app.
    private static readonly Dictionary<HotkeyGesture, string> Reserved = new()
    {
        [new(ModifierKeys.Control, Key.C)] = "Ctrl+C copies in every app. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.X)] = "Ctrl+X cuts in every app. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.V)] = "Ctrl+V is the smart paste itself. Pick another shortcut.",
        [new(ModifierKeys.Control | ModifierKeys.Shift, Key.V)] = "Ctrl+Shift+V always pastes your original. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.Z)] = "Ctrl+Z undoes in every app. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.Y)] = "Ctrl+Y redoes in every app. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.A)] = "Ctrl+A selects everything in every app. Pick another shortcut.",
        [new(ModifierKeys.Alt, Key.F4)] = "Alt+F4 closes windows. Pick another shortcut.",
        [new(ModifierKeys.Alt, Key.Tab)] = "Alt+Tab switches windows. Pick another shortcut.",
        [new(ModifierKeys.Alt, Key.Escape)] = "Alt+Esc switches windows. Pick another shortcut.",
        [new(ModifierKeys.Control, Key.Escape)] = "Ctrl+Esc opens Start. Pick another shortcut.",
        [new(ModifierKeys.Control | ModifierKeys.Alt, Key.Delete)] = "Ctrl+Alt+Delete belongs to Windows. Pick another shortcut.",
        [new(ModifierKeys.Windows, Key.L)] = "Win+L locks your PC. Pick another shortcut."
    };

    public bool HasRequiredModifier => (Modifiers & Required) != 0;

    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);

    // The keys as they appear on keycaps, in order: "Win", "Alt", "V".
    public IReadOnlyList<string> DisplayParts
    {
        get
        {
            var parts = ModifierNames(Modifiers);
            parts.Add(DisplayName(Key));
            return parts;
        }
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    // "Win", "Ctrl", "Alt", "Shift" for whatever is held, in stored order.
    public static List<string> ModifierNames(ModifierKeys modifiers)
    {
        var names = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Windows)) names.Add("Win");
        if (modifiers.HasFlag(ModifierKeys.Control)) names.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) names.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) names.Add("Shift");
        return names;
    }

    // Reads the stored form (the hotkey manager's canonical text).
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (!Hotkey.TryParse(text, out var hotkey)) return false;
        var key = KeyInterop.KeyFromVirtualKey(hotkey.VirtualKey);
        if (key == Key.None) return false;
        gesture = new HotkeyGesture(ToModifierKeys(hotkey.Modifiers), key);
        return true;
    }

    // Why this shortcut can't be used, or null when it can.
    public string? Problem()
    {
        if (!HasRequiredModifier) return "Hold Ctrl, Alt, or the Windows key, then press another key.";
        if (Reserved.GetValueOrDefault(this) is { } reason) return reason;
        if (!Hotkey.TryCreate(ToKeyModifiers(Modifiers), VirtualKey, out _, out var error))
            return error == HotkeyError.Reserved
                ? "Windows or every app already uses this shortcut. Pick another shortcut."
                : "This key can't be part of a shortcut. Pick another key.";
        return null;
    }

    // The stored form, for example "Win+Alt+V" or "Ctrl+Alt+Numpad4". Empty for a combination that can't be stored
    // (Problem() says why).
    public override string ToString() =>
        Hotkey.TryCreate(ToKeyModifiers(Modifiers), VirtualKey, out var hotkey, out _) ? hotkey.ToString() : "";

    private static KeyModifiers ToKeyModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Windows) ? KeyModifiers.Windows : KeyModifiers.None) |
        (modifiers.HasFlag(ModifierKeys.Control) ? KeyModifiers.Control : KeyModifiers.None) |
        (modifiers.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : KeyModifiers.None) |
        (modifiers.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : KeyModifiers.None);

    private static ModifierKeys ToModifierKeys(KeyModifiers modifiers) =>
        ((modifiers & KeyModifiers.Windows) != 0 ? ModifierKeys.Windows : ModifierKeys.None) |
        ((modifiers & KeyModifiers.Control) != 0 ? ModifierKeys.Control : ModifierKeys.None) |
        ((modifiers & KeyModifiers.Alt) != 0 ? ModifierKeys.Alt : ModifierKeys.None) |
        ((modifiers & KeyModifiers.Shift) != 0 ? ModifierKeys.Shift : ModifierKeys.None);

    // What the key shows on a keycap with the current keyboard layout ("." for the period key on a US layout).
    private static string DisplayName(Key key)
    {
        if (key is >= Key.OemSemicolon and <= Key.OemBackslash)
            return LayoutCharacter(key) ?? key.ToString();
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Snapshot => "Print Screen",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Decimal => "Num .",
            Key.Divide => "Num /",
            _ => key.ToString()
        };
    }

    private static string? LayoutCharacter(Key key)
    {
        var mapped = NativeMethods.MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(key), NativeMethods.MapVkToChar);
        // The high bit marks a dead key (an accent that combines with the next letter); keep only the character.
        var character = (char)(mapped & 0xFFFF);
        return mapped == 0 || char.IsControl(character) ? null : char.ToUpperInvariant(character).ToString();
    }

    private static class NativeMethods
    {
        public const uint MapVkToChar = 2;

        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        public static extern uint MapVirtualKey(uint code, uint mapType);
    }
}

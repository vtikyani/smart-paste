using System.Globalization;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

internal enum HotkeyError
{
    None,
    // No text at all.
    Empty,
    // Not "modifiers + key", or a modifier given twice.
    Malformed,
    // A key name this app does not know, or a modifier or lock key used as the key.
    UnknownKey,
    // Needs Ctrl, Alt, or Win; Shift alone would take a character away from typing.
    MissingModifier,
    // Belongs to Windows or to editing in every app (Ctrl+C, Alt+Tab, Win+L, Ctrl+Shift+V, ...).
    Reserved
}

// A global shortcut: one or more modifiers plus one key, written like "Win+Alt+V" or "Ctrl+Shift+F9". ToString gives
// the canonical form (Win, Ctrl, Alt, Shift, then the key), which TryParse reads back.
internal readonly record struct Hotkey
{
    private static readonly (KeyModifiers Modifier, string Name)[] ModifierOrder =
    [
        (KeyModifiers.Windows, "Win"),
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Shift, "Shift")
    ];

    private static readonly Dictionary<string, KeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Win"] = KeyModifiers.Windows,
        ["Windows"] = KeyModifiers.Windows,
        ["Ctrl"] = KeyModifiers.Control,
        ["Control"] = KeyModifiers.Control,
        ["Alt"] = KeyModifiers.Alt,
        ["Shift"] = KeyModifiers.Shift
    };

    private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();
    private static readonly Dictionary<string, int> KeysByName = BuildKeysByName();
    private static readonly HashSet<(KeyModifiers, int)> ReservedCombinations = BuildReserved();

    private Hotkey(KeyModifiers modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public KeyModifiers Modifiers { get; }

    public int VirtualKey { get; }

    // RegisterHotKey's MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN.
    internal uint NativeModifiers =>
        ((Modifiers & KeyModifiers.Alt) != 0 ? 0x1u : 0) |
        ((Modifiers & KeyModifiers.Control) != 0 ? 0x2u : 0) |
        ((Modifiers & KeyModifiers.Shift) != 0 ? 0x4u : 0) |
        ((Modifiers & KeyModifiers.Windows) != 0 ? 0x8u : 0);

    public static bool TryParse(string? text, out Hotkey hotkey) => TryParse(text, out hotkey, out _);

    public static bool TryParse(string? text, out Hotkey hotkey, out HotkeyError error)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = HotkeyError.Empty;
            return false;
        }

        var parts = text.Split('+');
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i].Trim();
            if (!ModifierNames.TryGetValue(part, out var modifier) || (modifiers & modifier) != 0)
            {
                error = HotkeyError.Malformed;
                return false;
            }
            modifiers |= modifier;
        }

        var keyName = parts[^1].Trim();
        if (keyName.Length == 0 || ModifierNames.ContainsKey(keyName))
        {
            error = HotkeyError.Malformed;
            return false;
        }
        if (!KeysByName.TryGetValue(keyName, out var virtualKey))
        {
            error = HotkeyError.UnknownKey;
            return false;
        }
        return TryCreate(modifiers, virtualKey, out hotkey, out error);
    }

    // For a shortcut recorder: validates a pressed combination.
    public static bool TryCreate(KeyModifiers modifiers, int virtualKey, out Hotkey hotkey, out HotkeyError error)
    {
        hotkey = default;
        if (!KeyNames.ContainsKey(virtualKey))
        {
            error = HotkeyError.UnknownKey;
            return false;
        }
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)) == 0)
        {
            error = HotkeyError.MissingModifier;
            return false;
        }
        if (ReservedCombinations.Contains((modifiers, virtualKey)))
        {
            error = HotkeyError.Reserved;
            return false;
        }
        hotkey = new Hotkey(modifiers, virtualKey);
        error = HotkeyError.None;
        return true;
    }

    public override string ToString()
    {
        if (!KeyNames.TryGetValue(VirtualKey, out var keyName)) return "";
        var parts = new List<string>(5);
        foreach (var (modifier, name) in ModifierOrder)
            if ((Modifiers & modifier) != 0) parts.Add(name);
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    // Canonical key names. Punctuation keys use their US-layout character, so "+" never needs to be a key name.
    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string>();
        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) names[c] = c.ToString();
        for (var f = 1; f <= 24; f++) names[0x6F + f] = "F" + f.ToString(CultureInfo.InvariantCulture);
        for (var n = 0; n <= 9; n++) names[0x60 + n] = "Numpad" + n.ToString(CultureInfo.InvariantCulture);
        names[0x08] = "Backspace";
        names[0x09] = "Tab";
        names[0x0D] = "Enter";
        names[0x13] = "Pause";
        names[0x1B] = "Esc";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2C] = "PrintScreen";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x6A] = "NumpadMultiply";
        names[0x6B] = "NumpadAdd";
        names[0x6D] = "NumpadSubtract";
        names[0x6E] = "NumpadDecimal";
        names[0x6F] = "NumpadDivide";
        names[0xBA] = ";";
        names[0xBB] = "=";
        names[0xBC] = ",";
        names[0xBD] = "-";
        names[0xBE] = ".";
        names[0xBF] = "/";
        names[0xC0] = "`";
        names[0xDB] = "[";
        names[0xDC] = "\\";
        names[0xDD] = "]";
        names[0xDE] = "'";
        return names;
    }

    private static Dictionary<string, int> BuildKeysByName()
    {
        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in KeyNames) keys[name] = key;
        keys["Return"] = 0x0D;
        keys["Escape"] = 0x1B;
        keys["PgUp"] = 0x21;
        keys["PgDn"] = 0x22;
        keys["PrtScn"] = 0x2C;
        keys["Ins"] = 0x2D;
        keys["Del"] = 0x2E;
        return keys;
    }

    private static HashSet<(KeyModifiers, int)> BuildReserved()
    {
        const KeyModifiers ctrl = KeyModifiers.Control, alt = KeyModifiers.Alt, shift = KeyModifiers.Shift, win = KeyModifiers.Windows;
        return
        [
            // Editing and the clipboard, in every app. Ctrl+Shift+V is the plain-text paste Smart Paste never touches.
            (ctrl, 'C'), (ctrl, 'X'), (ctrl, 'V'), (ctrl, 'Z'), (ctrl, 'Y'), (ctrl, 'A'), (ctrl, 0x2D),
            (ctrl | shift, 'V'), (ctrl | shift, 'Z'),
            // Switching windows, closing them, and the window menu.
            (alt, 0x09), (alt | shift, 0x09), (ctrl | alt, 0x09), (alt, 0x1B), (alt | shift, 0x1B), (alt, 0x73), (alt, 0x20),
            // Start, Task Manager, and the security screen.
            (ctrl, 0x1B), (ctrl | shift, 0x1B), (ctrl | alt, 0x2E),
            // Windows' own: lock, clipboard history, emoji, input language, task view, desktop, screen snip.
            (win, 'L'), (win, 'V'), (win, 0xBE), (win, 0xBA), (win, 0x20), (win, 0x09), (win, 'D'), (win | shift, 'S')
        ];
    }
}

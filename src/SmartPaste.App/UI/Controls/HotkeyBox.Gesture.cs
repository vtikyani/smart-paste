using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SmartPaste.App.UI.Controls;

// A global shortcut such as "Win+Alt+V": at least one of Ctrl, Alt, or the Windows key, plus one other key.
// ToString() is the form AppSettings stores (PickerHotkey, ToggleHotkey); the hotkey manager reads it back with
// TryParse. Modifiers are written in the order Windows uses in its own docs: Win, Ctrl, Alt, Shift.
internal readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    private const ModifierKeys Required = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows;

    // Stored names for keys whose enum name is ambiguous (Key.Next == Key.PageDown) or unfriendly (Key.D7).
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.D0] = "0", [Key.D1] = "1", [Key.D2] = "2", [Key.D3] = "3", [Key.D4] = "4",
        [Key.D5] = "5", [Key.D6] = "6", [Key.D7] = "7", [Key.D8] = "8", [Key.D9] = "9",
        [Key.NumPad0] = "Num0", [Key.NumPad1] = "Num1", [Key.NumPad2] = "Num2", [Key.NumPad3] = "Num3",
        [Key.NumPad4] = "Num4", [Key.NumPad5] = "Num5", [Key.NumPad6] = "Num6", [Key.NumPad7] = "Num7",
        [Key.NumPad8] = "Num8", [Key.NumPad9] = "Num9",
        [Key.Multiply] = "NumMultiply", [Key.Add] = "NumAdd", [Key.Subtract] = "NumSubtract",
        [Key.Decimal] = "NumDecimal", [Key.Divide] = "NumDivide",
        [Key.Return] = "Enter", [Key.Back] = "Backspace", [Key.Space] = "Space", [Key.Tab] = "Tab",
        [Key.PageUp] = "PageUp", [Key.PageDown] = "PageDown", [Key.Snapshot] = "PrintScreen",
        [Key.Scroll] = "ScrollLock", [Key.Pause] = "Pause", [Key.Insert] = "Insert", [Key.Delete] = "Delete",
        [Key.Home] = "Home", [Key.End] = "End", [Key.Up] = "Up", [Key.Down] = "Down", [Key.Left] = "Left",
        [Key.Right] = "Right", [Key.Apps] = "Menu",
        // Punctuation keys move around between keyboard layouts, so they're stored by position, not character.
        [Key.OemSemicolon] = "OemSemicolon", [Key.OemPlus] = "OemPlus", [Key.OemComma] = "OemComma",
        [Key.OemMinus] = "OemMinus", [Key.OemPeriod] = "OemPeriod", [Key.OemQuestion] = "OemQuestion",
        [Key.OemTilde] = "OemTilde", [Key.OemOpenBrackets] = "OemOpenBrackets", [Key.OemPipe] = "OemPipe",
        [Key.OemCloseBrackets] = "OemCloseBrackets", [Key.OemQuotes] = "OemQuotes", [Key.Oem8] = "Oem8",
        [Key.OemBackslash] = "OemBackslash"
    };

    private static readonly Dictionary<string, Key> KeysByName =
        Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    // Shortcuts that would break typing or Windows itself in every app, so they can't be taken.
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

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0)) return false;

        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "WIN" or "WINDOWS" => ModifierKeys.Windows,
                "CTRL" or "CONTROL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                _ => ModifierKeys.None
            };
            if (modifier == ModifierKeys.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }

        if (!TryParseKey(parts[^1], out var key) || IsModifierKey(key)) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return gesture.HasRequiredModifier;
    }

    // Why this shortcut can't be used, or null when it can.
    public string? Problem()
    {
        if (!HasRequiredModifier) return "Hold Ctrl, Alt, or the Windows key, then press another key.";
        return Reserved.GetValueOrDefault(this);
    }

    public override string ToString() => string.Join('+', [.. ModifierNames(Modifiers), StoredName(Key)]);

    private static string StoredName(Key key) => Names.GetValueOrDefault(key) ?? key.ToString();

    private static bool TryParseKey(string name, out Key key)
    {
        if (KeysByName.TryGetValue(name, out key)) return true;
        // Letters and F-keys are stored by their WPF key name. A bare number is not a key name.
        if (name.All(char.IsAsciiDigit)) return false;
        return Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    // What the key shows on a keycap with the current keyboard layout ("." for OemPeriod on a US layout).
    private static string DisplayName(Key key)
    {
        if (key is >= Key.OemSemicolon and <= Key.OemBackslash)
            return LayoutCharacter(key) ?? StoredName(key);
        return key switch
        {
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.Snapshot => "Print Screen",
            Key.Scroll => "Scroll Lock",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Decimal => "Num .",
            Key.Divide => "Num /",
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
            _ => StoredName(key)
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

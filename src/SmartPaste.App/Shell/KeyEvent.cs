using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// One keyboard event as the low-level hook reports it: enough to replay it exactly as it was typed.
internal readonly record struct KeyEvent(ushort VirtualKey, ushort ScanCode, bool IsUp, bool IsExtended)
{
    public static KeyEvent Down(ushort virtualKey, ushort scanCode = 0, bool extended = false) => new(virtualKey, scanCode, false, extended);

    public static KeyEvent Up(ushort virtualKey, ushort scanCode = 0, bool extended = false) => new(virtualKey, scanCode, true, extended);
}

internal static class VirtualKeys
{
    public const ushort Backspace = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Enter = 0x0D;
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Menu = 0x12;
    public const ushort Capital = 0x14;
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort Down = 0x28;
    public const ushort A = 0x41;
    public const ushort V = 0x56;
    public const ushort LeftWindows = 0x5B;
    public const ushort RightWindows = 0x5C;
    public const ushort NumLock = 0x90;
    public const ushort ScrollLock = 0x91;
    public const ushort LeftShift = 0xA0;
    public const ushort RightShift = 0xA1;
    public const ushort LeftControl = 0xA2;
    public const ushort RightControl = 0xA3;
    public const ushort LeftMenu = 0xA4;
    public const ushort RightMenu = 0xA5;

    // What the hook reports for characters injected with KEYEVENTF_UNICODE (on-screen keyboards, password managers).
    // The character itself travels in the scan code.
    public const ushort Packet = 0xE7;

    // Unassigned by Windows. Every replay batch ends with a release of this key so the hook can tell when the batch
    // has passed through; the hook swallows it, so no app ever sees it.
    public const ushort Fence = 0xE8;

    public static bool IsModifier(ushort virtualKey) => virtualKey is Shift or Control or Menu
        or LeftShift or RightShift or LeftControl or RightControl or LeftMenu or RightMenu or LeftWindows or RightWindows;

    public static bool IsLock(ushort virtualKey) => virtualKey is Capital or NumLock or ScrollLock;
}

// Which keys the user is holding, as seen by the hook. While the hook holds keys back the system's own key state
// (GetAsyncKeyState) is stale, because swallowed events never reach it.
internal sealed class KeyStateTracker
{
    private readonly bool[] _down = new bool[256];

    public bool ShiftDown => IsDown(VirtualKeys.LeftShift) || IsDown(VirtualKeys.RightShift) || IsDown(VirtualKeys.Shift);

    public bool ControlDown => IsDown(VirtualKeys.LeftControl) || IsDown(VirtualKeys.RightControl) || IsDown(VirtualKeys.Control);

    public bool AltDown => IsDown(VirtualKeys.LeftMenu) || IsDown(VirtualKeys.RightMenu) || IsDown(VirtualKeys.Menu);

    public bool WindowsDown => IsDown(VirtualKeys.LeftWindows) || IsDown(VirtualKeys.RightWindows);

    public KeyModifiers Modifiers =>
        (ShiftDown ? KeyModifiers.Shift : KeyModifiers.None) |
        (ControlDown ? KeyModifiers.Control : KeyModifiers.None) |
        (AltDown ? KeyModifiers.Alt : KeyModifiers.None) |
        (WindowsDown ? KeyModifiers.Windows : KeyModifiers.None);

    public bool IsDown(ushort virtualKey) => virtualKey < _down.Length && _down[virtualKey];

    public void Set(ushort virtualKey, bool down)
    {
        if (virtualKey < _down.Length) _down[virtualKey] = down;
    }

    public void Apply(in KeyEvent key) => Set(key.VirtualKey, !key.IsUp);
}

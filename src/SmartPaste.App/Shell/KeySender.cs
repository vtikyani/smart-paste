using System.Runtime.InteropServices;
using SmartPaste.App.Infrastructure;

namespace SmartPaste.App.Shell;

// Modifier keys the system reports as held, by side.
[Flags]
internal enum HeldModifiers
{
    None = 0,
    LeftControl = 1,
    RightControl = 2,
    LeftShift = 4,
    RightShift = 8,
    LeftAlt = 16,
    RightAlt = 32,
    LeftWindows = 64,
    RightWindows = 128
}

internal readonly record struct ScanCode(ushort Code, bool IsExtended);

// Injects keyboard input. Every event carries InjectedMarker, which the keyboard hook lets straight through, and
// each call is a single SendInput batch, which Windows never interleaves with other input.
internal static class KeySender
{
    // dwExtraInfo of everything Smart Paste injects ("SPST").
    public const nuint InjectedMarker = 0x5350_5354;

    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.Input>();

    // Shift, Alt, and Win are released (in this order) when held, so the app sees a plain Ctrl+V.
    private static readonly (HeldModifiers Flag, ushort Key)[] Releasable =
    [
        (HeldModifiers.LeftShift, VirtualKeys.LeftShift),
        (HeldModifiers.RightShift, VirtualKeys.RightShift),
        (HeldModifiers.LeftAlt, VirtualKeys.LeftMenu),
        (HeldModifiers.RightAlt, VirtualKeys.RightMenu),
        (HeldModifiers.LeftWindows, VirtualKeys.LeftWindows),
        (HeldModifiers.RightWindows, VirtualKeys.RightWindows)
    ];

    // Sends Ctrl+V to the focused app, whatever modifiers are physically down.
    public static bool SendPaste()
    {
        var layout = ForegroundLayout();
        return Send(BuildPaste(ReadHeldModifiers(), key => ScanCodeOf(key, layout)), "paste");
    }

    // Replays keys the hook held back, in one batch, followed by the fence the hook waits for (when given).
    public static bool Replay(IReadOnlyList<KeyEvent> keys, byte? fence) => Send(BuildReplay(keys, fence), "replay");

    // Ctrl is pressed first when it is not already down. Shift, Alt, and Win are then released while Ctrl is down:
    // releasing Alt or Win with another key pressed in between opens neither the menu bar nor Start.
    internal static NativeMethods.Input[] BuildPaste(HeldModifiers held, Func<ushort, ScanCode> scanCodeOf)
    {
        var inputs = new List<NativeMethods.Input>(10);
        var pressControl = (held & (HeldModifiers.LeftControl | HeldModifiers.RightControl)) == 0;
        if (pressControl) inputs.Add(Key(VirtualKeys.LeftControl, scanCodeOf, up: false));
        foreach (var (flag, key) in Releasable)
            if ((held & flag) != 0) inputs.Add(Key(key, scanCodeOf, up: true));
        inputs.Add(Key(VirtualKeys.V, scanCodeOf, up: false));
        inputs.Add(Key(VirtualKeys.V, scanCodeOf, up: true));
        if (pressControl) inputs.Add(Key(VirtualKeys.LeftControl, scanCodeOf, up: true));
        return inputs.ToArray();
    }

    // Each held event is replayed as it was typed: virtual key, scan code, extended flag, and direction. Characters
    // that arrived as KEYEVENTF_UNICODE input go back the same way.
    internal static NativeMethods.Input[] BuildReplay(IReadOnlyList<KeyEvent> keys, byte? fence)
    {
        var inputs = new NativeMethods.Input[keys.Count + (fence is null ? 0 : 1)];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var flags = key.IsUp ? NativeMethods.KeyeventfKeyUp : 0;
            inputs[i] = key.VirtualKey == VirtualKeys.Packet
                ? Keyboard(0, key.ScanCode, flags | NativeMethods.KeyeventfUnicode)
                : Keyboard(key.VirtualKey, key.ScanCode, flags | (key.IsExtended ? NativeMethods.KeyeventfExtendedKey : 0));
        }
        if (fence is { } id) inputs[^1] = Keyboard(VirtualKeys.Fence, id, NativeMethods.KeyeventfKeyUp);
        return inputs;
    }

    private static NativeMethods.Input Key(ushort virtualKey, Func<ushort, ScanCode> scanCodeOf, bool up)
    {
        var scan = scanCodeOf(virtualKey);
        var flags = (up ? NativeMethods.KeyeventfKeyUp : 0) | (scan.IsExtended ? NativeMethods.KeyeventfExtendedKey : 0);
        return Keyboard(virtualKey, scan.Code, flags);
    }

    private static NativeMethods.Input Keyboard(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeybdInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags,
                Time = 0,
                ExtraInfo = InjectedMarker
            }
        }
    };

    private static bool Send(NativeMethods.Input[] inputs, string kind)
    {
        if (inputs.Length == 0) return true;
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent == inputs.Length) return true;
        Log.Event("send_input_error", ("kind", kind), ("count", (int)sent), ("status", Marshal.GetLastPInvokeError()));
        return false;
    }

    private static HeldModifiers ReadHeldModifiers()
    {
        var held = HeldModifiers.None;
        if (IsDown(VirtualKeys.LeftControl)) held |= HeldModifiers.LeftControl;
        if (IsDown(VirtualKeys.RightControl)) held |= HeldModifiers.RightControl;
        if (IsDown(VirtualKeys.LeftShift)) held |= HeldModifiers.LeftShift;
        if (IsDown(VirtualKeys.RightShift)) held |= HeldModifiers.RightShift;
        if (IsDown(VirtualKeys.LeftMenu)) held |= HeldModifiers.LeftAlt;
        if (IsDown(VirtualKeys.RightMenu)) held |= HeldModifiers.RightAlt;
        if (IsDown(VirtualKeys.LeftWindows)) held |= HeldModifiers.LeftWindows;
        if (IsDown(VirtualKeys.RightWindows)) held |= HeldModifiers.RightWindows;
        return held;

        static bool IsDown(ushort key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    // The scan codes the foreground app's own keyboard layout uses, for apps that read them.
    private static IntPtr ForegroundLayout()
    {
        var thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        return NativeMethods.GetKeyboardLayout(thread);
    }

    private static ScanCode ScanCodeOf(ushort virtualKey, IntPtr layout)
    {
        var code = NativeMethods.MapVirtualKeyEx(virtualKey, NativeMethods.MapvkVkToVscEx, layout);
        return new ScanCode((ushort)(code & 0xFF), (code & 0xFF00) == 0xE000);
    }
}

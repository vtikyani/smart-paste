using System.Runtime.InteropServices;

namespace SmartPaste.App.Shell;

// Low-level hooks, the hook thread's message loop, input injection, and keyboard layout queries.
internal static partial class NativeMethods
{
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;

    public const int WmLButtonDown = 0x0201;
    public const int WmRButtonDown = 0x0204;
    public const int WmMButtonDown = 0x0207;
    public const int WmXButtonDown = 0x020B;

    public const uint LlkhfExtended = 0x01;
    public const uint LlkhfUp = 0x80;

    public const uint InputKeyboard = 1;
    public const uint KeyeventfExtendedKey = 0x0001;
    public const uint KeyeventfKeyUp = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;

    // MapVirtualKeyEx: virtual key to scan code, with an 0xE0 prefix for extended keys.
    public const uint MapvkVkToVscEx = 4;

    // ToUnicodeEx flag (Windows 10 1607+): translate without touching the keyboard state, so a pending dead key is
    // neither consumed nor changed.
    public const uint ToUnicodeKeepKeyboardState = 0x4;

    public const uint WmQuit = 0x0012;
    public const uint PmNoRemove = 0x0000;
    public const uint PmRemove = 0x0001;
    public const uint QsAllInput = 0x04FF;
    public const uint MwmoInputAvailable = 0x0004;
    public const uint Infinite = 0xFFFFFFFF;

    public delegate IntPtr LowLevelHookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int hookId, LowLevelHookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(
        uint virtualKey, uint scanCode, byte[] keyState, [Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out Msg message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsLlHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    // INPUT. The union includes MOUSEINPUT, its largest member, so the marshaled size matches what SendInput expects
    // on every architecture (40 bytes on x64 and ARM64).
    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeybdInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeybdInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }
}

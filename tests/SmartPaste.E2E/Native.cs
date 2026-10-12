using System.Runtime.InteropServices;
using System.Text;

namespace SmartPaste.E2E;

internal static class Native
{
    public const uint CfUnicodeText = 13;
    public const int VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkLWin = 0x5B, VkTab = 0x09, VkV = 0x56;
    private const uint InputKeyboard = 1, KeyeventfKeyup = 2, GmemMoveable = 2;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    public const uint WmClose = 0x0010;
    public const int GwlExStyle = -20, WsExNoActivate = 0x08000000;

    public static IntPtr RootOf(IntPtr hwnd) => hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, 2);

    // Brings a window to the front. Windows refuses SetForegroundWindow from a background process, so the
    // call is made while attached to the current foreground thread's input state.
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (RootOf(GetForegroundWindow()) == hwnd) return true;
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var thisThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, foregroundThread, false);
        }
        for (var i = 0; i < 40 && RootOf(GetForegroundWindow()) != hwnd; i++) Thread.Sleep(25);
        return RootOf(GetForegroundWindow()) == hwnd;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    public static (int Width, int Height) SizeOf(IntPtr hwnd) =>
        GetWindowRect(hwnd, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);

    public static List<IntPtr> VisibleWindowsOf(uint processId)
    {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && WindowProcess(hwnd) == processId) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool OpenClipboardRetry(IntPtr owner = default)
    {
        for (var i = 0; i < 40; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(25);
        }
        return false;
    }

    public static byte[]? CopyGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var size = GlobalSize(handle).ToUInt64();
        if (size == 0 || size > 64UL * 1024 * 1024) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public static bool SetGlobal(uint format, byte[] bytes)
    {
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)Math.Max(1, bytes.Length));
        if (handle == IntPtr.Zero) return false;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }
        try { Marshal.Copy(bytes, 0, ptr, bytes.Length); }
        finally { GlobalUnlock(handle); }
        if (SetClipboardData(format, handle) != IntPtr.Zero) return true;
        GlobalFree(handle);
        return false;
    }

    public static string? ReadClipboardText()
    {
        if (!OpenClipboardRetry()) return null;
        try
        {
            var bytes = CopyGlobal(GetClipboardData(CfUnicodeText));
            return bytes is null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CloseClipboard();
        }
    }

    // A plain text copy, the way any app copies text.
    public static void WriteClipboardText(string text)
    {
        if (!OpenClipboardRetry()) throw new InvalidOperationException("Clipboard is busy.");
        try
        {
            EmptyClipboard();
            if (!SetGlobal(CfUnicodeText, Encoding.Unicode.GetBytes(text + "\0")))
                throw new InvalidOperationException("Could not write the clipboard.");
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static uint WindowProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    // Types a key combination the way a person would (no injection marker), e.g. Chord(VkControl, VkV).
    public static void Chord(params int[] keys)
    {
        var inputs = new List<Input>();
        foreach (var key in keys) inputs.Add(Key(key, up: false));
        for (var i = keys.Length - 1; i >= 0; i--) inputs.Add(Key(keys[i], up: true));
        Send(inputs);
    }

    public static void Tap(int key) => Send([Key(key, false), Key(key, true)]);

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void Send(List<Input> inputs)
    {
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count) throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Count} events.");
    }

    private static Input Key(int vk, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeybdInput
        {
            Vk = (ushort)vk,
            Scan = (ushort)MapVirtualKey((uint)vk, 0),
            Flags = up ? KeyeventfKeyup : 0
        }
    };

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}

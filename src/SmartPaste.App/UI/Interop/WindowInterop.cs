using System.Runtime.InteropServices;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Interop;

// Window-level Win32 calls shared by the flyout and the popups: extended styles, placement in physical pixels,
// and the cursor position.
internal static class WindowInterop
{
    public const int ExTopmost = 0x00000008;
    public const int ExTransparent = 0x00000020;
    public const int ExToolWindow = 0x00000080;
    public const int ExLayered = 0x00080000;
    public const int ExNoActivate = 0x08000000;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    private const int GwlExStyle = -20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static void AddExtendedStyle(IntPtr hwnd, int style)
    {
        var current = GetWindowLong(hwnd, GwlExStyle).ToInt64();
        SetWindowLong(hwnd, GwlExStyle, new IntPtr(current | (uint)style));
    }

    // Moves a window without resizing or activating it, keeping it above other topmost windows.
    public static void MoveTopmost(IntPtr hwnd, int x, int y) =>
        NativeMethods.SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);

    // Moves a window without resizing, activating, or changing its z-order.
    public static void Move(IntPtr hwnd, int x, int y) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    public static ScreenRect GetBounds(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var r) ? ScreenRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : default;

    public static (int X, int Y) GetCursorPosition() =>
        NativeMethods.GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    // False when Windows refused, which it does unless the request follows the user's own input.
    public static bool BringToForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    private static IntPtr GetWindowLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? NativeMethods.GetWindowLongPtr64(hwnd, index) : NativeMethods.GetWindowLong32(hwnd, index);

    private static void SetWindowLong(IntPtr hwnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) NativeMethods.SetWindowLongPtr64(hwnd, index, value);
        else NativeMethods.SetWindowLong32(hwnd, index, value.ToInt32());
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X, Y;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        public static extern IntPtr GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        public static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}

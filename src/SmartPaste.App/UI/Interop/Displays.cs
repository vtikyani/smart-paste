using System.Runtime.InteropServices;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Interop;

// One monitor: its bounds and work area in physical pixels, and its effective DPI.
internal readonly record struct Display(ScreenRect Bounds, ScreenRect WorkArea, int Dpi)
{
    public double Scale => Dpi / 96.0;

    // Device-independent units (what WPF lays out in) to physical pixels on this monitor.
    public int ToPixels(double dip) => (int)Math.Ceiling(dip * Scale - 0.001);
}

// Finds the monitor under a point, rectangle, or window. The process is per-monitor DPI aware (app.manifest),
// so every rectangle here is in physical pixels.
internal static class Displays
{
    public static Display FromRect(ScreenRect rect)
    {
        var r = new NativeMethods.Rect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
        return FromMonitor(NativeMethods.MonitorFromRect(ref r, NativeMethods.MonitorDefaultToNearest));
    }

    public static Display FromPoint(int x, int y) =>
        FromMonitor(NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest));

    public static Display FromWindow(IntPtr hwnd) =>
        FromMonitor(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest));

    public static Display Primary =>
        FromMonitor(NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary));

    private static Display FromMonitor(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            // Only reachable while displays are being reconfigured; the primary screen size is a safe stand-in.
            var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxScreen);
            var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyScreen);
            var screen = new ScreenRect(0, 0, Math.Max(1, width), Math.Max(1, height));
            return new Display(screen, screen, 96);
        }
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? (int)dpiX
            : 96;
        return new Display(ToScreenRect(info.Monitor), ToScreenRect(info.Work), dpi);
    }

    private static ScreenRect ToScreenRect(NativeMethods.Rect r) => ScreenRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

    private static class NativeMethods
    {
        public const uint MonitorDefaultToPrimary = 1;
        public const uint MonitorDefaultToNearest = 2;
        public const int MdtEffectiveDpi = 0;
        public const int SmCxScreen = 0;
        public const int SmCyScreen = 1;

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

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}

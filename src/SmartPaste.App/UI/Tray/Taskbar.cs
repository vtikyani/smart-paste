using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Tray;

internal enum TaskbarEdge { Left, Top, Right, Bottom }

// Where the taskbar is, in physical pixels.
internal readonly record struct TaskbarPosition(TaskbarEdge Edge, ScreenRect Bounds);

// Facts about the primary taskbar, which hosts the notification area: its position, its own light or dark theme,
// and the size the shell draws tray icons at.
internal static class Taskbar
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static TaskbarPosition? Position()
    {
        var data = new NativeMethods.AppBarData { Size = Marshal.SizeOf<NativeMethods.AppBarData>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) == IntPtr.Zero) return null;
        var edge = data.Edge switch
        {
            0 => TaskbarEdge.Left,
            1 => TaskbarEdge.Top,
            2 => TaskbarEdge.Right,
            _ => TaskbarEdge.Bottom
        };
        return new TaskbarPosition(edge, ScreenRect.FromLTRB(data.Rect.Left, data.Rect.Top, data.Rect.Right, data.Rect.Bottom));
    }

    // The taskbar follows the Windows mode, not the app mode; in high contrast it uses the system window colors.
    public static TaskbarTheme Theme()
    {
        if (SystemParameters.HighContrast)
        {
            var c = SystemColors.WindowColor;
            return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 128 ? TaskbarTheme.Light : TaskbarTheme.Dark;
        }
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        // Missing on builds that predate light taskbars, which were always dark.
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0 ? TaskbarTheme.Light : TaskbarTheme.Dark;
    }

    // DPI of the monitor holding the primary taskbar.
    public static int Dpi()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var monitor = taskbar != IntPtr.Zero
            ? NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToPrimary)
            : NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0 ? (int)dpiX : 96;
    }

    // The small-icon size Windows uses for tray icons at a DPI: 16 px at 100 %, 20 at 125 %, 24 at 150 %...
    public static int IconSize(int dpi)
    {
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, (uint)dpi);
        return size > 0 ? size : (int)Math.Round(16 * dpi / 96.0);
    }

    // The large-icon size at a DPI (32 px at 100 %), used for notification icons.
    public static int LargeIconSize(int dpi)
    {
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxIcon, (uint)dpi);
        return size > 0 ? size : (int)Math.Round(32 * dpi / 96.0);
    }

    private static class NativeMethods
    {
        public const uint AbmGetTaskbarPos = 0x00000005;
        public const uint MonitorDefaultToPrimary = 1;
        public const int SmCxIcon = 11;
        public const int SmCxSmIcon = 49;

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
        public struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public Rect Rect;
            public IntPtr Param;
        }

        [DllImport("shell32.dll")]
        public static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string className, string? windowName);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int index, uint dpi);
    }
}

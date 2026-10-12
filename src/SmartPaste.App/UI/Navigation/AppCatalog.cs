using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartPaste.App.UI.Navigation;

// An app as the privacy page shows it. Icon is frozen, so it can cross threads.
internal sealed record RunningApp(string ExeName, string DisplayName, ImageSource? Icon);

// Which apps are running, with their names and icons, for the excluded-apps list.
internal interface IAppCatalog
{
    // Apps that have a window on screen, one entry per executable, sorted by name. Never Smart Paste itself.
    IReadOnlyList<RunningApp> GetRunningApps();

    // The running app with this executable name ("slack.exe"), or null when it isn't running.
    RunningApp? Find(string exeName);
}

// Reads running processes with the least access Windows offers (PROCESS_QUERY_LIMITED_INFORMATION), so it works
// for apps running as another user or as admin too. Safe to call from any thread.
internal sealed class RunningAppCatalog : IAppCatalog
{
    public static readonly RunningAppCatalog Instance = new();

    private RunningAppCatalog()
    {
    }

    public IReadOnlyList<RunningApp> GetRunningApps()
    {
        var processIds = new HashSet<int>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd) && NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) != 0)
                processIds.Add((int)pid);
            return true;
        }, IntPtr.Zero);
        processIds.Remove(Environment.ProcessId);

        var apps = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in processIds)
        {
            if (ImagePath(pid) is not { } path) continue;
            var exe = Path.GetFileName(path);
            if (!apps.ContainsKey(exe)) apps[exe] = Describe(exe, path);
        }
        return apps.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public RunningApp? Find(string exeName)
    {
        var name = Path.GetFileNameWithoutExtension(exeName);
        if (name.Length == 0) return null;
        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch (InvalidOperationException) { return null; }
        try
        {
            foreach (var process in processes)
                if (ImagePath(process.Id) is { } path) return Describe(Path.GetFileName(path), path);
            return null;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static RunningApp Describe(string exe, string path)
    {
        string? description = null;
        try { description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); }
        catch (FileNotFoundException) { }
        var name = string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exe) : description;
        return new RunningApp(exe, name, LoadIcon(path));
    }

    // Like the Alt+Tab list: visible, not owned by another window, not a tool window, not hidden by the system.
    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero) return false;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        if ((exStyle & NativeMethods.WsExToolWindow) != 0) return false;
        if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string? ImagePath(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static ImageSource? LoadIcon(string path)
    {
        var info = new NativeMethods.ShFileInfo();
        var result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
            NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static class NativeMethods
    {
        public const int GwOwner = 4;
        public const int GwlExStyle = -20;
        public const long WsExToolWindow = 0x00000080;
        public const int DwmwaCloaked = 14;
        public const uint ProcessQueryLimitedInformation = 0x1000;
        public const uint ShgfiIcon = 0x000000100;
        public const uint ShgfiLargeIcon = 0x000000000;

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
        public static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ShFileInfo
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }
}

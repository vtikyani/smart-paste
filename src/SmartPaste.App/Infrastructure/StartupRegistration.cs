using Microsoft.Win32;

namespace SmartPaste.App.Infrastructure;

// "Start at login" through the per-user Run key. No admin rights needed, and Task Manager's Startup tab
// shows and controls it like any other app.
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmartPaste";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value, Command(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, Command());
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // Applies the setting, unless this is an isolated test or development copy, which must never register
    // itself to start at login.
    public static void Apply(bool enabled)
    {
        if (AppPaths.IsIsolated) return;
        if (enabled) Enable();
        else Disable();
    }

    private static string Command() => $"\"{AppInfo.ExecutablePath}\"";
}

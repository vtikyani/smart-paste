using Microsoft.Win32;

namespace SmartPaste.App;
internal static class StartupRegistration
{
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool IsEnabled()
    {
        using var key=Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("SmartPaste") is string value && value==QuotedPath();
    }
    internal static void Enable()
    {
        using var key=Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue("SmartPaste",QuotedPath());
    }
    internal static void Disable()
    {
        using var key=Registry.CurrentUser.OpenSubKey(Key,true);
        key?.DeleteValue("SmartPaste",false);
    }
    private static string QuotedPath() => "\""+Environment.ProcessPath+"\"";
}

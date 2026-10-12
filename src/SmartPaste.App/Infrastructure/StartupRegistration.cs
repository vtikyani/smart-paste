using System.Security;
using Microsoft.Win32;

namespace SmartPaste.App.Infrastructure;

// "Start at login" through the per-user Run key. No admin rights needed, and Task Manager's Startup tab
// shows and controls it like any other app.
//
// Windows keeps its own on/off switch for each entry, set from Task Manager or Settings > Apps > Startup, in
// StartupApproved\Run. An entry switched off there doesn't start, whatever the Run key says, so it counts as off here
// too. Turning it on in Smart Paste clears that switch; Smart Paste's own housekeeping at start never does.
internal static class StartupRegistration
{
    // Tells the app it was started at sign-in, so it stays in the tray instead of opening a window.
    public const string BackgroundArgument = "--background";

    // True when Smart Paste will start at the next sign-in.
    public static bool IsEnabled() => IsEnabled(RegistryStartupValues.Instance, Command());

    public static void Enable() => Enable(RegistryStartupValues.Instance, Command());

    public static void Disable() => Disable(RegistryStartupValues.Instance);

    // Applies the user's choice, unless this is an isolated test or development copy, which must never register
    // itself to start at login.
    public static void Apply(bool enabled)
    {
        if (AppPaths.IsIsolated) return;
        if (enabled) Enable();
        else Disable();
    }

    // At every start: keeps the entry pointing at this copy, or removes it, without undoing an "off" the user chose
    // in Windows.
    public static void Refresh(bool enabled)
    {
        if (AppPaths.IsIsolated) return;
        if (enabled) Refresh(RegistryStartupValues.Instance, Command());
        else Disable();
    }

    // What the "Start when I sign in" switches show. An isolated copy never registers itself, so for it, and when
    // the registry can't be read, the saved setting is the answer.
    public static bool IsEnabledOr(bool saved)
    {
        if (AppPaths.IsIsolated) return saved;
        try
        {
            return IsEnabled();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("startup_read_error", ex);
            return saved;
        }
    }

    internal static bool IsEnabled(IStartupValues values, string command) =>
        string.Equals(values.ReadRunCommand(), command, StringComparison.OrdinalIgnoreCase) &&
        !StartupApproval.IsDisabled(values.ReadApproval());

    // The user turned it on in Smart Paste: an "off" set earlier in Windows would still keep it from starting, and
    // the user just asked for the opposite.
    internal static void Enable(IStartupValues values, string command)
    {
        values.WriteRunCommand(command);
        if (StartupApproval.IsDisabled(values.ReadApproval())) values.WriteApproval(StartupApproval.Enabled);
    }

    internal static void Refresh(IStartupValues values, string command)
    {
        if (!string.Equals(values.ReadRunCommand(), command, StringComparison.OrdinalIgnoreCase)) values.WriteRunCommand(command);
    }

    // Removes Windows' switch along with the entry, so nothing is left behind.
    internal static void Disable(IStartupValues values)
    {
        values.DeleteRunCommand();
        values.DeleteApproval();
    }

    private static string Command() => $"\"{AppInfo.ExecutablePath}\" {BackgroundArgument}";
}

// Windows' on/off switch for one startup entry: binary, first byte even when on (02, 06) and odd when switched off
// (03, 07); the next eight bytes hold when it was switched off.
internal static class StartupApproval
{
    // What Windows writes when an entry is switched back on.
    public static byte[] Enabled => [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static bool IsDisabled(byte[]? value) => value is { Length: > 0 } && (value[0] & 1) == 1;
}

// The Run entry and Windows' switch for it, behind an interface so the rules above can be tested without the registry.
internal interface IStartupValues
{
    string? ReadRunCommand();

    void WriteRunCommand(string command);

    void DeleteRunCommand();

    byte[]? ReadApproval();

    void WriteApproval(byte[] value);

    void DeleteApproval();
}

// The real values, in HKEY_CURRENT_USER.
internal sealed class RegistryStartupValues : IStartupValues
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "SmartPaste";

    public static readonly RegistryStartupValues Instance = new();

    private RegistryStartupValues()
    {
    }

    public string? ReadRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public void WriteRunCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, command);
    }

    public void DeleteRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public byte[]? ReadApproval()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(ValueName) as byte[];
    }

    public void WriteApproval(byte[] value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        key.SetValue(ValueName, value, RegistryValueKind.Binary);
    }

    public void DeleteApproval()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

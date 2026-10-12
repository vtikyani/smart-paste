namespace SmartPaste.Core;

public static class ClipboardPrivacy
{
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    public const string CloudFormat = "CanUploadToCloudClipboard";

    // Password managers, by the executable names their vendors ship (checked against each project's build files or
    // published paths). A copy they own is never read, and Ctrl+V into them is never intercepted. Most of them also
    // mark copied secrets with ExcludeFormat, which catches renamed portable copies and apps missing here; this list
    // is the second line of defence.
    public static readonly IReadOnlySet<string> BlockedOwnerProcesses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1Password.exe",
            "Bitwarden.exe",
            "Buttercup.exe",
            "Enpass.exe",
            "iCloudPasswords.exe",
            "KeePass.exe",
            "KeePassXC.exe",
            "keeperpasswordmanager.exe",
            "KeeWeb.exe",
            "kpm.exe",
            "NordPass.exe",
            "passbolt.exe",
            "PasswordDepot.exe",
            "ProtonPass.exe",
            "pwsafe.exe",
            "RoboTaskBarIcon.exe",
            "robotaskbaricon-x64.exe",
            "SafeInCloud.exe",
            "stpass.exe"
        };

    public static bool ShouldSkip(
        IReadOnlyCollection<string> formatNames,
        Func<string, uint?> readDword,
        string? ownerProcessName)
    {
        if (!string.IsNullOrWhiteSpace(ownerProcessName))
        {
            var owner = ownerProcessName.Trim();
            if (!owner.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) owner += ".exe";
            if (BlockedOwnerProcesses.Contains(owner)) return true;
        }

        var formats = new HashSet<string>(formatNames, StringComparer.OrdinalIgnoreCase);
        if (formats.Contains(ExcludeFormat)) return true;
        if (formats.Contains(HistoryFormat) && readDword(HistoryFormat) != 1) return true;
        if (formats.Contains(CloudFormat) && readDword(CloudFormat) != 1) return true;
        return false;
    }
}

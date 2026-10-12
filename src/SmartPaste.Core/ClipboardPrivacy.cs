namespace SmartPaste.Core;

public static class ClipboardPrivacy
{
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    public const string CloudFormat = "CanUploadToCloudClipboard";

    // Password managers. A copy they own is never read, and Ctrl+V into them is never intercepted. Most of them also
    // mark copied secrets with ExcludeFormat; this list is the second line of defence. An entry that matches no real
    // process costs nothing, so names are kept even where only the vendor's installer confirmed them.
    public static readonly IReadOnlySet<string> BlockedOwnerProcesses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1Password.exe",
            "Bitwarden.exe",
            "Enpass.exe",
            "KeePass.exe",
            "KeePassXC.exe",
            "keeperpasswordmanager.exe",
            "NordPass.exe",
            "Proton Pass.exe",
            "pwsafe.exe",
            "RoboTaskBarIcon.exe"
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

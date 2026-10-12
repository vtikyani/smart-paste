namespace SmartPaste.App.Infrastructure;

// Runs from the installer's uninstall hook, before the app files are removed. Leaves nothing behind:
// saved API keys, the login entry, settings, learned labels, and logs all go.
internal static class UninstallCleanup
{
    public static void Run()
    {
        TryRun(CredentialStore.DeleteAll);
        TryRun(StartupRegistration.Disable);
        TryRun(DeleteData);
    }

    private static void DeleteData()
    {
        foreach (var file in new[] { AppPaths.SettingsFile, AppPaths.LabelCacheFile })
            if (File.Exists(file)) File.Delete(file);
        if (Directory.Exists(AppPaths.LogDirectory)) Directory.Delete(AppPaths.LogDirectory, recursive: true);
    }

    // Uninstall must finish even if one step fails (a file in use, a key already gone).
    private static void TryRun(Action step)
    {
        try { step(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }
}

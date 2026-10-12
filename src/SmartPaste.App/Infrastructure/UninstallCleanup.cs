namespace SmartPaste.App.Infrastructure;

// Runs from the installer's uninstall hook, before the app files are removed. Leaves nothing behind:
// saved API keys, the login entry, settings, learned labels, logs, and temp files from interrupted saves all go.
internal static class UninstallCleanup
{
    // Temp files the settings and label stores write before renaming (left behind if a save was interrupted).
    private static readonly string[] TempFilePatterns = [".settings.*.tmp", ".label-cache.json.*.tmp"];

    public static void Run()
    {
        TryRun(CredentialStore.DeleteAll);
        TryRun(StartupRegistration.Disable);
        // One step per item: a file still in use must not keep the others from going.
        TryDelete(AppPaths.SettingsFile);
        TryDelete(AppPaths.LabelCacheFile);
        if (Directory.Exists(AppPaths.DataDirectory))
            foreach (var pattern in TempFilePatterns)
                foreach (var file in SafeEnumerate(AppPaths.DataDirectory, pattern))
                    TryDelete(file);
        TryRun(() =>
        {
            if (Directory.Exists(AppPaths.LogDirectory)) Directory.Delete(AppPaths.LogDirectory, recursive: true);
        });
        // The installer removes only its own folder, which is separate from the data folder, so the data folder goes
        // here too. It is removed only when empty: nothing but Smart Paste's own files is ever deleted.
        TryRun(() =>
        {
            if (Directory.Exists(AppPaths.DataDirectory) && !Directory.EnumerateFileSystemEntries(AppPaths.DataDirectory).Any())
                Directory.Delete(AppPaths.DataDirectory);
        });
    }

    // Retries briefly: the app was only just stopped, and a file can stay locked for a moment.
    private static void TryDelete(string file)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Uninstall must finish even if one step fails (a file in use, a key already gone).
    private static void TryRun(Action step)
    {
        try { step(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }
}

namespace SmartPaste.App.UI.Settings.Pages;

// Turns what someone types into the excluded-apps box into the program file name Smart Paste compares against.
internal static class ExcludedAppNames
{
    // "Slack", "slack.exe", "\"C:\Program Files\Slack\slack.exe\"" all become "slack.exe" (case kept as typed).
    // On failure, error says what to change.
    public static bool TryNormalize(string? input, IEnumerable<string> excluded, IEnumerable<string> protectedApps,
        out string exeName, out string error)
    {
        exeName = "";
        error = "";
        var text = (input ?? "").Trim().Trim('"').Trim();
        if (text.IndexOfAny(['\\', '/']) >= 0) text = text[(text.LastIndexOfAny(['\\', '/']) + 1)..];
        if (text.Length == 0)
        {
            error = "Type the app's program name, like slack.exe.";
            return false;
        }
        if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Program names can't contain characters like * ? : < > or |.";
            return false;
        }
        if (!text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) text += ".exe";
        if (text.Length == ".exe".Length)
        {
            error = "Type the app's program name, like slack.exe.";
            return false;
        }

        var name = text;
        if (protectedApps.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Smart Paste already leaves {name} alone.";
            return false;
        }
        if (excluded.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"{name} is already on your list.";
            return false;
        }
        exeName = name;
        return true;
    }
}

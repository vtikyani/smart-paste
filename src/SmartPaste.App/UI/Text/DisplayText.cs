namespace SmartPaste.App.UI.Text;

// Small text transforms shared by the flyout and the popups.
internal static class DisplayText
{
    // What a hidden value looks like. Always the same length, so nothing about the value leaks, not even its size.
    public const string Masked = "••••••••";

    // "just now", "4 min ago", "2 h ago", "3 d ago". Halves round up, as people read clocks.
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromSeconds(45)) return "just now";
        if (elapsed < TimeSpan.FromMinutes(59.5)) return $"{Math.Max(1, Nearest(elapsed.TotalMinutes))} min ago";
        if (elapsed < TimeSpan.FromHours(23.5)) return $"{Nearest(elapsed.TotalHours)} h ago";
        return $"{Nearest(elapsed.TotalDays)} d ago";
    }

    private static int Nearest(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // The keys of a shortcut as written in settings, for key caps: "Win+Alt+V" becomes Win, Alt, V.
    public static IReadOnlyList<string> Keys(string? shortcut) =>
        string.IsNullOrWhiteSpace(shortcut)
            ? []
            : shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // A one-line preview of a value: line breaks become spaces and long values end with an ellipsis.
    public static string OneLine(string value, int maxLength)
    {
        var flat = string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= maxLength ? flat : flat[..Math.Max(1, maxLength - 1)].TrimEnd() + "…";
    }

    // "shipping address" becomes "Shipping address"; entity labels come from the parser in lower case.
    public static string Sentence(string label)
    {
        var trimmed = label.Trim();
        return trimmed.Length == 0 ? trimmed : char.ToUpper(trimmed[0], System.Globalization.CultureInfo.CurrentCulture) + trimmed[1..];
    }

    // "chrome.exe" becomes "chrome".
    public static string AppName(string app) =>
        app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app;
}

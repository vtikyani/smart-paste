using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.App.UI.Text;

// Release notes arrive as Markdown (the CHANGELOG section of the release). The About page shows them as plain text:
// headings lose their #s, list items get a bullet, links keep only their text, and emphasis marks go.
internal static partial class ReleaseNotesText
{
    // Enough to say what's new without turning the About page into a changelog.
    public const int MaxLines = 16;

    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = raw.Trim();
            var line = Heading().Match(trimmed) is { Success: true } heading ? Inline(heading.Groups[1].Value)
                : Bullet().Match(trimmed) is { Success: true } bullet ? "• " + Inline(bullet.Groups[1].Value)
                : Inline(trimmed);
            // One blank line between blocks at most, and none at the start.
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line);
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > MaxLines) lines = [.. lines.Take(MaxLines), "…"];
        return string.Join('\n', lines);
    }

    private static string Inline(string text)
    {
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        var plain = new StringBuilder(text.Length);
        foreach (var c in text.Replace("**", "").Replace("__", ""))
            if (c is not ('`' or '*')) plain.Append(c);
        return plain.ToString().Trim();
    }

    [GeneratedRegex(@"^#{1,6}\s+(.*?)\s*#*$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-+*•]\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}

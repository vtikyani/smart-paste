using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public static partial class LabelNormalizer
{
    public static string Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var value = RequiredTag().Replace(label.ToLowerInvariant(), " ");
        value = RequiredWord().Replace(value, " ");
        var builder = new StringBuilder(value.Length);
        foreach (var c in value) builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    public static string Humanize(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId)) return "";
        var value = CamelBoundary().Replace(automationId, "$1 $2");
        value = AcronymBoundary().Replace(value, "$1 $2");
        return Normalize(value);
    }

    [GeneratedRegex(@"\((?:required|optional)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredTag();
    [GeneratedRegex(@"\brequired\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredWord();
    [GeneratedRegex(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();
    [GeneratedRegex(@"([A-Z])([A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundary();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

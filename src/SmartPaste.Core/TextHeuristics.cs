using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public sealed record FastFields(string? Email, string? Phone, string? Zip, string? Website, string? Linkedin);

public static class TextHeuristics
{
    public const int MaxChars = 20_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly Regex Email = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", Options, RegexTimeout);
    private static readonly Regex Phone = new(@"(?<!\d)(?:\+\d[\d\s().-]{7,}\d|(?:\+?1[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4})(?!\d)", Options, RegexTimeout);
    private static readonly Regex UsZip = new(@"\b\d{5}(?:-\d{4})?\b", Options, RegexTimeout);
    private static readonly Regex CanadianZip = new(@"\b[A-Z]\d[A-Z] ?\d[A-Z]\d\b", Options, RegexTimeout);
    private static readonly Regex Street = new(@"\b\d+\s+(?:[\p{L}0-9.'#-]+\s+){1,8}(?:St|Street|Ave|Avenue|Rd|Road|Blvd|Dr|Drive|Ln|Lane|Ct|Way|Pl|Pkwy|Hwy|Ter|Cir)\b", Options, RegexTimeout);
    private static readonly Regex Url = new(@"\b(?:https?://|www\.|linkedin\.com/)[^\s,;]+", Options, RegexTimeout);
    private static readonly Regex LinkedinUrl = new(@"\b(?:https?://)?(?:www\.)?linkedin\.com/[^\s,;]+", Options, RegexTimeout);
    private static readonly Regex Date = new(@"\b(?:\d{1,2}/\d{1,2}/\d{2,4}|\d{4}-\d{2}-\d{2}|(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4})\b", Options, RegexTimeout);

    public static FastFields Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var phones = Phone.Matches(text).Cast<Match>().Where(m => m.Success).ToArray();
        var zip = UsZip.Matches(text).Cast<Match>()
            .Concat(CanadianZip.Matches(text).Cast<Match>())
            .Where(m => m.Success && !phones.Any(p => m.Index < p.Index + p.Length && p.Index < m.Index + m.Length))
            .OrderBy(m => m.Index).FirstOrDefault()?.Value;

        return new FastFields(
            First(Email, text),
            phones.FirstOrDefault()?.Value,
            zip,
            First(Url, text)?.TrimEnd('.', ')'),
            First(LinkedinUrl, text)?.TrimEnd('.', ')'));
    }

    public static int CountFieldTypes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fast = Extract(text);
        var count = 0;
        if (fast.Email is not null) count++;
        if (fast.Phone is not null) count++;
        if (fast.Zip is not null) count++;
        if (Street.IsMatch(text)) count++;
        if (fast.Website is not null) count++;
        if (Date.IsMatch(text)) count++;
        return count;
    }

    public static bool LooksMultiField(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxChars && CountFieldTypes(text) >= 2;

    private static string? First(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Value : null;
    }
}

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

    // How many kinds of contact details the text holds (email, phone, street, postal code, link, date).
    public static int CountFieldTypes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fast = Extract(text);
        var count = 0;
        if (fast.Email is not null) count++;
        if (fast.Phone is not null) count++;
        var street = AddressPatterns.FindStreet(text) is not null;
        if (street) count++;
        // A lone five-digit number (an order number) is not a postal code; one after a state, or near a street, is.
        if (fast.Zip is not null && (street || AddressPatterns.FindCityStateZip(text) is not null)) count++;
        if (fast.Website is not null) count++;
        if (Date.IsMatch(text)) count++;
        return count;
    }

    // True for a block of someone's details: at least two kinds of contact details, or at least two recognized
    // "Label: value" lines. Ordinary prose with one date or one phone number is not.
    public static bool LooksMultiField(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars) return false;
        try
        {
            return CountFieldTypes(text) >= 2 || LabelLines.CountRecognized(text) >= 2;
        }
        catch (RegexMatchTimeoutException)
        {
            // Text this convoluted is not a block of contact details.
            return false;
        }
    }

    private static string? First(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Value : null;
    }
}

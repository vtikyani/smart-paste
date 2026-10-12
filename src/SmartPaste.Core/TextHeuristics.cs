using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public sealed record FastFields(string? Email, string? Phone, string? Zip, string? Website, string? Linkedin);

// Quick reads of copied text: whether it looks like someone's contact details, and the values whose shape alone says
// what they are (an email address, a phone number, a postal code by its city, links).
public static partial class TextHeuristics
{
    public const int MaxChars = 20_000;
    private const int TimeoutMs = 200;

    // A phone number written like one: a US or Canadian number with separators or brackets ("201-555-0100",
    // "(201) 555-0100", "+1 201 555 0100"), or a number with a country code ("+44 20 7946 0958"). A bare run of digits
    // ("1234567890") is not one, nor is a number joined to more digits ("112-3456789-0123456"). Only spaces and tabs
    // separate its parts, never a line break.
    private const string PhonePattern =
        @"(?<![\w+]|\d[-.])(?:(?:\+?1[ \t.-]?)?(?:\(\d{3}\)[ \t]?|\d{3}[ \t.-])\d{3}[ \t.-]\d{4}|\+[1-9](?:[ \t.()-]{0,2}\d){7,14})(?![-.]?\d)";

    // The number on a line labeled as a phone ("Mobile 2015550100", "Tel.: 020 7946 0958"), however it is written.
    private const string LabeledPhonePattern =
        @"^[ \t]*(?:(?:mobile|cell|work|home|office|daytime|evening|direct|main|primary|business|personal|contact)[ \t]+)?" +
        @"(?:phone|mobile|mob|cell|cellphone|tel|telephone|ph)\b\.?(?:[ \t]*(?:number|num|no\.?|#))?[ \t]*[:：=-]?[ \t]*" +
        @"(?<phone>\+?\(?\d(?:[ \t.()-]{0,2}\d){6,14})(?![ \t.()-]{0,2}\d)";

    // Each part has a length limit, so a long run of letters with no "@" is rejected quickly.
    private const string EmailPattern = @"(?<![\w.%+-])[A-Za-z0-9._%+-]{1,64}@(?:[A-Za-z0-9-]{1,63}\.){1,8}[A-Za-z]{2,24}(?![\w-])";

    // Links end before spaces, brackets, quotes, and list punctuation: "<https://janedoe.dev>" is "https://janedoe.dev".
    private const string LinkPattern =
        @"(?<![\w@./-])(?:(?:https?://|www\.)|(?:[a-z]{2,3}\.)?linkedin\.com/)[^\s<>""'`|,;]+";

    private const string LinkedinPattern = @"(?<![\w@./-])(?:https?://)?(?:www\.|[a-z]{2,3}\.)?linkedin\.com/[^\s<>""'`|,;]+";

    public static FastFields Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = LabelLines.SplitLines(text);
        return new FastFields(
            FindEmail(text),
            FindPhone(lines),
            AddressPatterns.Read(lines).Place?.Zip,
            FindLink(LinkRegex(), text),
            FindLink(LinkedinRegex(), text));
    }

    // True for a block of someone's contact details: at least two personal contact signals (CountContactSignals). Prose
    // with one phone number, a log line with a date and an order number, or an email header is not.
    public static bool LooksMultiField(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars) return false;
        try
        {
            return CountContactSignals(text) >= 2;
        }
        catch (RegexMatchTimeoutException)
        {
            // Text this convoluted is not a block of contact details.
            return false;
        }
    }

    // How many kinds of personal contact details the text holds, each counted once: an email address; a phone number
    // written like one, or on a line labeled as one; a street address; a postal code next to its city or state, or under
    // a street; and lines labeled as a name, email, phone, address, city, state, or ZIP code. A company label counts only
    // next to another of these. Dates and links never count, and lines whose label is sensitive or about someone else
    // (emergency contact, reference) are not read.
    internal static int CountContactSignals(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var block = LabelLines.Read(text);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var labeled in block.Labeled)
            if (ContactKind(labeled.Mapping.Key, labeled.Line.Value) is { } kind) kinds.Add(kind);
        if (block.Free.Any(line => EmailRegex().IsMatch(line))) kinds.Add(FieldKeys.Email);
        if (FindPhone(block.Free) is not null) kinds.Add(FieldKeys.Phone);
        var address = AddressPatterns.Read(block.Free);
        if (address.Street is not null) kinds.Add(FieldKeys.Street);
        if (address.Place is not null) kinds.Add(FieldKeys.Zip);
        var company = kinds.Remove(FieldKeys.Company);
        return kinds.Count + (company && kinds.Count > 0 ? 1 : 0);
    }

    // The first phone number: one written like a phone number, or the number on a line labeled as one.
    internal static string? FindPhone(IReadOnlyList<string> lines)
    {
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = MaskLinks(raw);
            var written = PhoneRegex().Match(line);
            var labeled = LabeledPhoneRegex().Match(line);
            if (labeled.Success && (!written.Success || labeled.Groups["phone"].Index <= written.Index))
                return raw.Substring(labeled.Groups["phone"].Index, labeled.Groups["phone"].Length);
            if (written.Success) return raw.Substring(written.Index, written.Length);
        }
        return null;
    }

    // The phone number in a value labeled as a phone: the first one written like a phone number, else the first run of
    // 7 to 15 digits ("2015550100").
    internal static string? PhoneIn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var written = PhoneRegex().Match(value);
        if (written.Success) return written.Value;
        var bare = BarePhoneRegex().Match(value);
        return bare.Success ? bare.Value : null;
    }

    // The first email address in the text.
    internal static string? FindEmail(string text)
    {
        var match = EmailRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    // Every email address in the text.
    internal static IEnumerable<string> FindEmails(string text) => EmailRegex().Matches(text).Select(match => match.Value);

    // True when the whole value is a phone number written like one.
    internal static bool IsWrittenPhone(string value) => WholePhoneRegex().IsMatch(value);

    // True when the whole value is an email address.
    internal static bool IsEmail(string value) => WholeEmailRegex().IsMatch(value);

    // The text with links and email addresses blanked (same length), so their digits are never read as a postal code,
    // a house number, or a phone number.
    internal static string MaskLinks(string text)
    {
        var masked = LinkRegex().Replace(text, match => new string(' ', match.Length));
        return EmailRegex().Replace(masked, match => new string(' ', match.Length));
    }

    private static string? FindLink(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? TrimLink(match.Value) : null;
    }

    // "https://janedoe.dev." and "(see https://janedoe.dev)" end before their punctuation; a link's own brackets stay
    // ("https://en.wikipedia.org/wiki/Mercury_(planet)").
    internal static string TrimLink(string link)
    {
        var end = link.Length;
        while (end > 0)
        {
            var c = link[end - 1];
            var unbalanced = c == ')' && link.AsSpan(0, end).Count('(') < link.AsSpan(0, end).Count(')');
            if (c is '.' or ':' or '!' or '?' or ']' or '}' or '*' || unbalanced) end--;
            else break;
        }
        return link[..end];
    }

    // The kind of contact detail a labeled line gives, when its label is a contact label and its value fits it.
    private static string? ContactKind(string key, string value) => key switch
    {
        FieldKeys.FirstName or FieldKeys.MiddleName or FieldKeys.LastName or FieldKeys.FullName or FieldKeys.City or FieldKeys.State
            => value.Any(char.IsLetter) ? key : null,
        FieldKeys.Email => EmailRegex().IsMatch(value) ? key : null,
        FieldKeys.Phone => value.Count(char.IsAsciiDigit) >= 7 ? key : null,
        FieldKeys.Street or FieldKeys.Unit => value.Any(char.IsLetterOrDigit) ? FieldKeys.Street : null,
        FieldKeys.Zip => value.Any(char.IsAsciiDigit) && value.Trim().Length <= 10 ? key : null,
        FieldKeys.Company => value.Any(char.IsLetterOrDigit) ? key : null,
        _ => null
    };

    [GeneratedRegex(PhonePattern, RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[ \t]*" + PhonePattern + @"[ \t]*$", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex WholePhoneRegex();

    [GeneratedRegex(LabeledPhonePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex LabeledPhoneRegex();

    [GeneratedRegex(@"(?<![\w+])\+?\(?\d(?:[ \t.()-]{0,2}\d){6,14}(?![ \t.()-]{0,2}\d)", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex BarePhoneRegex();

    [GeneratedRegex(EmailPattern, RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[ \t]*" + EmailPattern + @"[ \t]*$", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex WholeEmailRegex();

    [GeneratedRegex(LinkPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(LinkedinPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex LinkedinRegex();
}

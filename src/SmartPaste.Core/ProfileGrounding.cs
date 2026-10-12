using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Checks that the values a parsing model returns appear in the text the user copied, so a model mistake can never
// put invented details into a form. A value must appear whole: whole words, a whole email address, whole groups of a
// number, a whole link, never a piece of one ("jordan@example.co" is not in "jordan@example.com", nor "201-555-010" in
// "201-555-0100"). Matching forgives case, spacing, punctuation, accents ("Montreal" for "Montréal"), and periods in
// abbreviations ("NJ" for "N.J."). A name matches when each of its words appears; a state or country matches its code
// or name; a phone number may add a leading country code 1.
public sealed partial class ProfileGrounding
{
    private const int TimeoutMs = 200;

    // Characters that end a link in running text.
    private static readonly char[] LinkSeparators = [' ', '\t', '\r', '\n', '<', '>', '"', '\'', '`', '|', ',', ';'];

    // The source's words, plain (lower case, no accents, abbreviations closed up), padded for whole-word search.
    private readonly string _padded;
    private readonly string[] _tokens;
    private readonly HashSet<string> _words;
    // Words as written (case kept) with dotted abbreviations closed up: "N.J." is "NJ".
    private readonly HashSet<string> _codes;
    private readonly HashSet<string> _emails;
    private readonly HashSet<string> _links;
    // The digit groups of each number: "(201) 555-0100" is 201, 555, 0100.
    private readonly List<string[]> _numbers;
    private readonly DateTime[] _dates;

    public ProfileGrounding(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        try
        {
            var closed = CloseAbbreviations(sourceText);
            var plain = PlainWords(closed);
            _padded = " " + plain + " ";
            _tokens = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _codes = WordBreaks().Split(closed).Where(word => word.Length > 0).ToHashSet(StringComparer.Ordinal);
            _emails = TextHeuristics.FindEmails(sourceText).Select(email => email.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            _links = LinkForms(sourceText);
            _numbers = DigitRun().Matches(sourceText).Select(match => DigitGroups().Matches(match.Value).Select(g => g.Value).ToArray()).ToList();
            _dates = DateText().Matches(sourceText)
                .Select(match => ValueFormatter.TryParseDate(match.Value, out var date) ? date : (DateTime?)null)
                .OfType<DateTime>().ToArray();
        }
        catch (RegexMatchTimeoutException)
        {
            // Text too convoluted to read grounds nothing: every value the model returns for it is dropped.
            _padded = " ";
            _tokens = [];
            _codes = [];
            _emails = [];
            _links = [];
            _numbers = [];
            _dates = [];
        }
        _words = new HashSet<string>(_tokens, StringComparer.Ordinal);
    }

    // The entity with only the values found in the source text.
    public Entity Ground(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entity.Fields)
            if (IsGrounded(key, value)) fields[key] = value;
        return new Entity(entity.Label, fields);
    }

    public bool IsGrounded(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            return Appears(key, value);
        }
        catch (RegexMatchTimeoutException)
        {
            // A value too convoluted to check is not kept.
            return false;
        }
    }

    private bool Appears(string key, string value)
    {
        // A short state or country code must appear in capitals: "IN" is not grounded by the word "in".
        if (key is FieldKeys.State or FieldKeys.Country && value.Count(char.IsLetter) <= 3)
            return key == FieldKeys.State ? RegionAppears(value) : CountryAppears(value);
        return key switch
        {
            FieldKeys.Email => _emails.Contains(value.Trim().ToLowerInvariant()),
            FieldKeys.Phone => PhoneAppears(value),
            FieldKeys.Zip => PostalCodeAppears(value),
            FieldKeys.Website or FieldKeys.Linkedin => LinkAppears(value),
            FieldKeys.DateOfBirth => (ValueFormatter.TryParseDate(value, out var date) && _dates.Contains(date)) || WordsAppear(value),
            FieldKeys.FirstName or FieldKeys.MiddleName or FieldKeys.LastName or FieldKeys.FullName => WordsAppear(value) || AllWordsAppear(value),
            FieldKeys.State => WordsAppear(value) || RegionAppears(value),
            FieldKeys.Country => WordsAppear(value) || CountryAppears(value),
            _ => WordsAppear(value)
        };
    }

    // The value's words, in order, as whole words of the source.
    private bool WordsAppear(string value)
    {
        var plain = Plain(value);
        return plain.Length > 0 && _padded.Contains(" " + plain + " ", StringComparison.Ordinal);
    }

    // "Rivera Jordan" for a copied "Jordan Rivera" or "Rivera, Jordan".
    private bool AllWordsAppear(string value)
    {
        var words = Plain(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(_words.Contains);
    }

    // At least seven digits that are whole, neighboring groups of one number in the text. "+1 201 555 0100" for a
    // copied "201-555-0100": the country code may be added.
    private bool PhoneAppears(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 7) return false;
        return NumberAppears(digits) || (digits.Length == 11 && digits[0] == '1' && NumberAppears(digits[1..]));
    }

    private bool NumberAppears(string digits)
    {
        foreach (var groups in _numbers)
        {
            for (var i = 0; i < groups.Length; i++)
            {
                var joined = new StringBuilder(digits.Length);
                for (var j = i; j < groups.Length && joined.Length < digits.Length; j++)
                    joined.Append(groups[j]);
                if (joined.Length == digits.Length && joined.ToString() == digits) return true;
            }
        }
        return false;
    }

    // A postal code as its words ("K1A 0B1", "07081") or without its space or dash ("K1A0B1", "070811234"), over whole,
    // neighboring words of the text: "55501" is not in "201-555-0100".
    private bool PostalCodeAppears(string value)
    {
        var plain = Plain(value);
        if (!plain.Any(char.IsAsciiDigit)) return false;
        if (WordsAppear(value)) return true;
        var compact = plain.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length < 3) return false;
        for (var i = 0; i < _tokens.Length; i++)
        {
            var joined = "";
            for (var j = i; j < _tokens.Length && j < i + 3 && joined.Length < compact.Length; j++)
                joined += _tokens[j];
            if (joined == compact) return true;
        }
        return false;
    }

    // A whole link of the text, or its site without the rest of the path ("jrivera.dev" for "jrivera.dev/about").
    // Links compare without scheme, "www.", or a trailing slash.
    private bool LinkAppears(string value)
    {
        var form = LinkForm(value);
        if (form.Length <= 3) return false;
        return _links.Contains(form) || _links.Any(link =>
            link.Length > form.Length && link.StartsWith(form, StringComparison.Ordinal) && link[form.Length] is '/' or '?' or '#');
    }

    // "NJ" for a copied "New Jersey" or "N.J.", and the reverse. Short codes must appear in capitals.
    private bool RegionAppears(string value) =>
        CodeAppears(value) ||
        (ValueFormatter.TryRegion(value, out var code, out var name) && (CodeAppears(code) || WordsAppear(name)));

    private bool CountryAppears(string value)
    {
        if (CodeAppears(value)) return true;
        if (!ValueFormatter.TryCountry(value, out var code, out var name)) return false;
        if (CodeAppears(code) || WordsAppear(name)) return true;
        return new[] { "USA", "UK" }.Any(alias =>
            ValueFormatter.TryCountry(alias, out var aliasCode, out _) && aliasCode == code && CodeAppears(alias));
    }

    private bool CodeAppears(string code) => _codes.Contains(code.Trim().Replace(".", "", StringComparison.Ordinal));

    // Lower case, no accents, abbreviations closed up, letters and digits only, single spaces:
    // "123 Main St., Montréal, N.J." -> "123 main st montreal nj".
    private static string Plain(string value) => PlainWords(CloseAbbreviations(value));

    private static string PlainWords(string value)
    {
        string decomposed;
        try
        {
            decomposed = value.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Text with broken Unicode is compared as it is.
            decomposed = value;
        }
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }
        return builder.ToString().TrimEnd();
    }

    // "N.J." -> "NJ", "U.S.A." -> "USA".
    private static string CloseAbbreviations(string value) =>
        DottedAbbreviation().Replace(value, match => match.Value.Replace(".", "", StringComparison.Ordinal));

    private static HashSet<string> LinkForms(string text)
    {
        var links = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in text.Split(LinkSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var form = LinkForm(TextHeuristics.TrimLink(token.TrimStart('(', '[', '{')));
            if (form.Length > 3 && form.Contains('.', StringComparison.Ordinal)) links.Add(form);
        }
        return links;
    }

    private static string LinkForm(string value)
    {
        var form = value.Trim().ToLowerInvariant();
        foreach (var prefix in new[] { "https://", "http://" })
            if (form.StartsWith(prefix, StringComparison.Ordinal)) form = form[prefix.Length..];
        if (form.StartsWith("www.", StringComparison.Ordinal)) form = form[4..];
        return form.TrimEnd('/');
    }

    // Digits that belong together: "(201) 555-0100", "+1 201.555.0100", "07081-1234".
    [GeneratedRegex(@"\+?\(?\d(?:[\d().-]|[ ](?=[\d(]))*\d", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex DigitGroups();

    [GeneratedRegex(@"\b(?:\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?,?\s+\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex DateText();

    [GeneratedRegex(@"(?<![\p{L}\p{N}.])\p{L}(?:\.\p{L})+\.?(?![\p{L}\p{N}])", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex DottedAbbreviation();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex WordBreaks();
}

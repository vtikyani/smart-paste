using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Checks that the values a parsing model returns appear in the text the user copied, so a model mistake can never
// put invented details into a form. Matching forgives case, spacing, and punctuation; phone numbers and ZIP codes
// match on their digits; a name matches when each of its words appears; a state or country matches its code or name.
public sealed partial class ProfileGrounding
{
    private readonly string _raw;
    private readonly string _loose;
    private readonly string _urls;
    private readonly string _compact;
    private readonly HashSet<string> _words;
    private readonly string[] _digitRuns;
    private readonly DateTime[] _dates;

    public ProfileGrounding(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        _raw = sourceText;
        _loose = Loose(sourceText);
        _urls = UrlForm(sourceText);
        _compact = new string(sourceText.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        _words = new HashSet<string>(_loose.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        _digitRuns = DigitRun().Matches(sourceText).Select(m => Digits(m.Value)).ToArray();
        _dates = DateText().Matches(sourceText)
            .Select(m => ValueFormatter.TryParseDate(m.Value, out var date) ? date : (DateTime?)null)
            .OfType<DateTime>().ToArray();
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
        // A short state or country code must match in capitals: "IN" is not grounded by the word "in".
        if (key is FieldKeys.State or FieldKeys.Country && value.Count(char.IsLetter) <= 3)
            return key == FieldKeys.State ? RegionAppears(value) : CountryAppears(value);
        var loose = Loose(value);
        if (loose.Length > 0 && ContainsWords(_loose, loose)) return true;
        return key switch
        {
            FieldKeys.Email => _raw.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase),
            FieldKeys.Phone => DigitsAppear(value, 7),
            FieldKeys.Zip => DigitsAppear(value, 5) || CompactAppears(value, 6),
            FieldKeys.Website or FieldKeys.Linkedin => UrlForm(value) is { Length: > 3 } url && _urls.Contains(url, StringComparison.Ordinal),
            FieldKeys.FirstName or FieldKeys.MiddleName or FieldKeys.LastName or FieldKeys.FullName => AllWordsAppear(loose),
            FieldKeys.State => RegionAppears(value),
            FieldKeys.Country => CountryAppears(value),
            FieldKeys.DateOfBirth => ValueFormatter.TryParseDate(value, out var date) && _dates.Contains(date),
            _ => false
        };
    }

    private bool DigitsAppear(string value, int minimum)
    {
        var digits = Digits(value);
        if (digits.Length < minimum) return false;
        if (_digitRuns.Any(run => run.Contains(digits, StringComparison.Ordinal))) return true;
        // "+1 201 555 0100" for a copied "201-555-0100": the country code may be added.
        return digits.Length == 11 && digits[0] == '1' && _digitRuns.Any(run => run.Contains(digits[1..], StringComparison.Ordinal));
    }

    // "K1A0B1" for a copied "K1A 0B1".
    private bool CompactAppears(string value, int minimum)
    {
        var compact = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return compact.Length >= minimum && _compact.Contains(compact, StringComparison.Ordinal);
    }

    // "Rivera Jordan" for a copied "Jordan Rivera" or "Rivera, Jordan".
    private bool AllWordsAppear(string loose)
    {
        var words = loose.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(_words.Contains);
    }

    // "NJ" for a copied "New Jersey" and the reverse. Short codes must appear in capitals ("in" and "or" are words).
    private bool RegionAppears(string value) =>
        CodeAppears(value.Trim()) ||
        (ValueFormatter.TryRegion(value, out var code, out var name) && (CodeAppears(code) || ContainsWords(_loose, Loose(name))));

    private bool CountryAppears(string value)
    {
        if (CodeAppears(value.Trim())) return true;
        if (!ValueFormatter.TryCountry(value, out var code, out var name)) return false;
        if (CodeAppears(code) || ContainsWords(_loose, Loose(name))) return true;
        return new[] { "USA", "U.S.", "U.S.A.", "UK", "U.K." }.Any(alias =>
            ValueFormatter.TryCountry(alias, out var aliasCode, out _) && aliasCode == code && CodeAppears(alias));
    }

    private bool CodeAppears(string code) =>
        Regex.IsMatch(_raw, @"(?<![\p{L}\p{N}])" + Regex.Escape(code) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);

    private static bool ContainsWords(string text, string words) =>
        (" " + text + " ").Contains(" " + words + " ", StringComparison.Ordinal);

    // Lower case, letters and digits only, single spaces: "123 Main St., Apt 4B" -> "123 main st apt 4b".
    private static string Loose(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Links compare without scheme, "www." or a trailing slash.
    private static string UrlForm(string value) =>
        UrlNoise().Replace(value.Trim().ToLowerInvariant(), "").TrimEnd('/');

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    // Digits that belong together: "(201) 555-0100", "+1 201.555.0100", "07081-1234".
    [GeneratedRegex(@"\+?\(?\d(?:[\d().-]|[ ](?=[\d(]))*\d", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\b(?:\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?,?\s+\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateText();

    [GeneratedRegex(@"https?://|www\.", RegexOptions.CultureInvariant)]
    private static partial Regex UrlNoise();
}

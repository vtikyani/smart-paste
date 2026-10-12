using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Shapes a value for the field it goes into: NJ vs New Jersey, phone and date patterns, ZIP vs ZIP+4. Hints come
// from the field's label, help text, placeholder, and id. Never throws; when unsure, the value is left as it is.
public static partial class ValueFormatter
{
    private static readonly Dictionary<string, string> Regions = BuildRegions();
    private static readonly Dictionary<string, string> RegionCodesByName =
        Regions.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Countries = BuildCountries();
    private static readonly Dictionary<string, string> CountryCodesByName = BuildCountryNames();

    private static readonly string[] DateInputFormats =
    [
        "M/d/yyyy", "M/d/yy", "M-d-yyyy", "yyyy-M-d", "yyyy/M/d", "d.M.yyyy", "MMMM d, yyyy", "MMMM d yyyy",
        "d MMMM yyyy", "d MMMM, yyyy", "MMM d, yyyy", "MMM d yyyy", "d MMM yyyy", "MMM. d, yyyy"
    ];

    public static string Format(string key, string value, FieldContext field)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var raw = string.Join(" | ", new[]
            {
                field.LabeledBy, field.Name, field.HelpText, field.Placeholder,
                LabelNormalizer.Humanize(field.AutomationId ?? ""), field.OcrText
            }.Where(h => !string.IsNullOrWhiteSpace(h)));
        var words = LabelNormalizer.Normalize(raw);
        try
        {
            return key switch
            {
                FieldKeys.Phone => FormatPhone(value, raw, words, field.MaxLength),
                FieldKeys.DateOfBirth => FormatDate(value, raw),
                FieldKeys.Zip => FormatZip(value, words, field.MaxLength),
                FieldKeys.State => FormatRegion(value, words, field),
                FieldKeys.Country => FormatCountry(value, words, field),
                _ => value
            };
        }
        catch (FormatException)
        {
            // Formatting is a nicety; the unformatted value is always a valid answer.
            return value;
        }
    }

    // "NJ", "N.J." or "New Jersey" -> ("NJ", "New Jersey"). US states and territories, Canadian provinces.
    internal static bool TryRegion(string value, out string code, out string name)
    {
        var text = value.Trim().Replace(".", "", StringComparison.Ordinal);
        if (Regions.TryGetValue(text, out name!)) { code = text.ToUpperInvariant(); return true; }
        if (RegionCodesByName.TryGetValue(text, out code!)) { name = Regions[code]; return true; }
        code = name = "";
        return false;
    }

    // "US", "USA", "U.S." or "United States" -> ("US", "United States").
    internal static bool TryCountry(string value, out string code, out string name)
    {
        var text = value.Trim().TrimEnd('.');
        if (text.Length == 2 && Countries.TryGetValue(text, out name!)) { code = text.ToUpperInvariant(); return true; }
        if (CountryCodesByName.TryGetValue(text, out code!)) { name = Countries[code]; return true; }
        code = name = "";
        return false;
    }

    internal static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(Spaces().Replace(value.Trim(), " "), DateInputFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out date);

    private static string FormatPhone(string value, string raw, string words, int? maxLength)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        // Too few digits to be a phone number: nothing to reshape.
        if (digits.Length < 7) return value;
        // US and Canada numbers: 10 digits, or 11 with the leading country code 1.
        string? local = digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits.Length == 10 ? digits : null;
        if (local is not null && PhoneMask().Match(raw) is { Success: true } mask) return FillMask(mask.Value, local);
        if (HasAny(words, "digits", "digits only", "numbers only", "numeric", "no dashes", "no spaces")) return local ?? digits;
        if (local is not null && HasAny(words, "e 164", "international", "country code")) return "+1" + local;
        if (maxLength is int max && max > 0 && value.Length > max)
        {
            var fits = local is null
                ? new[] { digits }
                : new[] { $"({local[..3]}) {local[3..6]}-{local[6..]}", $"{local[..3]}-{local[3..6]}-{local[6..]}", local };
            foreach (var candidate in fits)
                if (candidate.Length <= max) return candidate;
        }
        return value;
    }

    // Writes the number into a pattern from the hints: "(555) 555-5555", "555.555.5555", "+1 ### ### ####".
    private static string FillMask(string mask, string local)
    {
        var builder = new StringBuilder(mask.Length);
        var start = 0;
        if (CountryPrefix().Match(mask) is { Success: true } prefix)
        {
            builder.Append(prefix.Value);
            start = prefix.Length;
        }
        var next = 0;
        for (var i = start; i < mask.Length; i++)
        {
            var c = mask[i];
            if (char.IsAsciiDigit(c) || c is 'X' or 'x' or '#')
            {
                if (next == local.Length) return local;
                builder.Append(local[next++]);
            }
            else builder.Append(c);
        }
        return next == local.Length ? builder.ToString() : local;
    }

    private static string FormatDate(string value, string raw)
    {
        if (!TryParseDate(value, out var date)) return value;
        var format = DatePattern(raw);
        return format is null ? value : date.ToString(format, CultureInfo.InvariantCulture);
    }

    // A .NET format for the date pattern in the hints ("MM/DD/YYYY", "dd.mm.yyyy", "YYYY-MM-DD", "MM/YY",
    // "Month DD, YYYY"), or null when the hints name none.
    private static string? DatePattern(string raw)
    {
        if (DateMask().Match(raw) is { Success: true } mask)
        {
            var parts = DateToken().Matches(mask.Value).Select(m => m.Value.ToLowerInvariant()).ToArray();
            // "MM DD YYYY" written with spaces most often means slashes.
            var separator = mask.Value.FirstOrDefault(c => c is '/' or '-' or '.');
            if (separator == default) separator = '/';
            var tokens = parts.Select(p => p switch
            {
                "mm" => "MM", "m" => "M", "dd" => "dd", "d" => "d", "yyyy" => "yyyy", "yy" => "yy", _ => null
            }).ToArray();
            var kinds = tokens.Select(t => t?[0]).ToArray();
            // A usable pattern names the month, has no part twice, and has a day or a year.
            if (tokens.All(t => t is not null) && kinds.Contains('M') && kinds.Distinct().Count() == kinds.Length &&
                (kinds.Contains('d') || kinds.Contains('y')))
                return string.Join($"'{separator}'", tokens);
        }
        if (MonthNamePattern().Match(raw) is { Success: true } named)
            return named.Groups["dayFirst"].Success ? "d MMMM yyyy" : "MMMM d, yyyy";
        return null;
    }

    private static string FormatZip(string value, string words, int? maxLength)
    {
        var text = value.Trim();
        if (UsZip().Match(text) is { Success: true } us)
        {
            var five = us.Groups["five"].Value;
            var four = us.Groups["four"].Success ? us.Groups["four"].Value : null;
            if (maxLength == 5 || HasAny(words, "5 digit", "5 digits", "five digit")) return five;
            if (four is null) return value;
            if (maxLength == 9) return five + four;
            if (maxLength is >= 10 || HasAny(words, "zip 4", "zip plus 4", "plus 4", "9 digit")) return $"{five}-{four}";
            if (maxLength is > 5 and < 9) return five;
            return value;
        }
        if (CanadianPostal().Match(text) is { Success: true } ca)
        {
            var first = ca.Groups["first"].Value.ToUpperInvariant();
            var second = ca.Groups["second"].Value.ToUpperInvariant();
            return maxLength == 6 ? first + second : $"{first} {second}";
        }
        return value;
    }

    private static string FormatRegion(string value, string words, FieldContext field)
    {
        if (!TryRegion(value, out var code, out var name)) return value;
        var example = PlaceholderExample(field.Placeholder);
        if (HasAny(words, "abbr", "abbreviation", "abbreviated", "code", "2 letter", "2 letters", "two letter") ||
            field.MaxLength is > 0 and <= 3 ||
            (example is { Length: 2 } && Regions.ContainsKey(example)))
            return code;
        if (HasAny(words, "full", "name", "spelled out") ||
            (example is { Length: > 3 } && RegionCodesByName.ContainsKey(example)) ||
            (IsComboBox(field) && field.MaxLength is null or > 3))
            return name;
        return value;
    }

    private static string FormatCountry(string value, string words, FieldContext field)
    {
        if (!TryCountry(value, out var code, out var name)) return value;
        var example = PlaceholderExample(field.Placeholder);
        if (HasAny(words, "code", "iso", "2 letter", "two letter") || field.MaxLength == 2 ||
            (example is { Length: 2 } && Countries.ContainsKey(example)))
            return code;
        // Abbreviations ("US", "U.S.A.", "UK") read as names in a country field unless the field asks for a code.
        var abbreviation = value.Count(char.IsLetter) <= 4;
        if (IsComboBox(field) || HasAny(words, "name") || abbreviation) return name;
        return value;
    }

    // "e.g. NJ" -> "NJ", "Example: New Jersey" -> "New Jersey".
    private static string? PlaceholderExample(string? placeholder)
    {
        if (string.IsNullOrWhiteSpace(placeholder)) return null;
        var text = ExamplePrefix().Replace(placeholder.Trim(), "").Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsComboBox(FieldContext field) =>
        field.ControlType?.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasAny(string words, params string[] terms) => terms.Any(t => Has(words, t));
    private static bool Has(string words, string term) => (" " + words + " ").Contains(" " + term + " ", StringComparison.Ordinal);

    [GeneratedRegex(@"(?:\+\s?1[\s.-]?)?(?:\(\s?[0-9Xx#]{3}\s?\)|[0-9Xx#]{3})[\s.-]?[0-9Xx#]{3}[\s.-]?[0-9Xx#]{4}(?![0-9Xx#])", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneMask();
    [GeneratedRegex(@"^\+\s?1[\s.-]?", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPrefix();
    [GeneratedRegex(@"(?<![A-Za-z])(?:mm?|dd?|yyyy|yy)(?:\s?[/.\- ]\s?(?:mm?|dd?|yyyy|yy)){1,2}(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateMask();
    [GeneratedRegex(@"yyyy|yy|mm|m|dd|d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateToken();
    [GeneratedRegex(@"(?<dayFirst>\b(?:dd?|day)\s+month\b)|\bmonth\s+(?:dd?|day)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthNamePattern();
    [GeneratedRegex(@"^(?<five>\d{5})(?:[\s-]?(?<four>\d{4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex UsZip();
    [GeneratedRegex(@"^(?<first>[A-Za-z]\d[A-Za-z])[\s-]?(?<second>\d[A-Za-z]\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex CanadianPostal();
    [GeneratedRegex(@"^(?:e\.?\s?g\.?|ex\.?|example|for example)\s*[:,]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePrefix();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    private static Dictionary<string, string> BuildRegions()
    {
        const string pairs = "AL:Alabama|AK:Alaska|AZ:Arizona|AR:Arkansas|CA:California|CO:Colorado|CT:Connecticut|DE:Delaware|FL:Florida|GA:Georgia|HI:Hawaii|ID:Idaho|IL:Illinois|IN:Indiana|IA:Iowa|KS:Kansas|KY:Kentucky|LA:Louisiana|ME:Maine|MD:Maryland|MA:Massachusetts|MI:Michigan|MN:Minnesota|MS:Mississippi|MO:Missouri|MT:Montana|NE:Nebraska|NV:Nevada|NH:New Hampshire|NJ:New Jersey|NM:New Mexico|NY:New York|NC:North Carolina|ND:North Dakota|OH:Ohio|OK:Oklahoma|OR:Oregon|PA:Pennsylvania|RI:Rhode Island|SC:South Carolina|SD:South Dakota|TN:Tennessee|TX:Texas|UT:Utah|VT:Vermont|VA:Virginia|WA:Washington|WV:West Virginia|WI:Wisconsin|WY:Wyoming|DC:District of Columbia|PR:Puerto Rico|GU:Guam|VI:U.S. Virgin Islands|AS:American Samoa|MP:Northern Mariana Islands|AB:Alberta|BC:British Columbia|MB:Manitoba|NB:New Brunswick|NL:Newfoundland and Labrador|NS:Nova Scotia|NT:Northwest Territories|NU:Nunavut|ON:Ontario|PE:Prince Edward Island|QC:Quebec|SK:Saskatchewan|YT:Yukon";
        return pairs.Split('|').Select(p => p.Split(':', 2)).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    // Every state and province name, longest first, for address patterns.
    internal static IEnumerable<string> RegionNames => Regions.Values.OrderByDescending(n => n.Length);

    private static Dictionary<string, string> BuildCountries()
    {
        const string pairs = "US:United States|CA:Canada|GB:United Kingdom|AU:Australia|NZ:New Zealand|IN:India|DE:Germany|FR:France|ES:Spain|IT:Italy|NL:Netherlands|BE:Belgium|CH:Switzerland|AT:Austria|SE:Sweden|NO:Norway|DK:Denmark|FI:Finland|IE:Ireland|PT:Portugal|GR:Greece|PL:Poland|CZ:Czechia|SK:Slovakia|HU:Hungary|RO:Romania|BG:Bulgaria|HR:Croatia|SI:Slovenia|EE:Estonia|LV:Latvia|LT:Lithuania|UA:Ukraine|TR:Turkey|RU:Russia|JP:Japan|CN:China|KR:South Korea|TW:Taiwan|HK:Hong Kong|SG:Singapore|MY:Malaysia|TH:Thailand|VN:Vietnam|PH:Philippines|ID:Indonesia|PK:Pakistan|BD:Bangladesh|AE:United Arab Emirates|SA:Saudi Arabia|IL:Israel|EG:Egypt|ZA:South Africa|NG:Nigeria|KE:Kenya|MX:Mexico|BR:Brazil|AR:Argentina|CL:Chile|CO:Colombia|PE:Peru|UY:Uruguay|CR:Costa Rica|PA:Panama|JM:Jamaica";
        return pairs.Split('|').Select(p => p.Split(':', 2)).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildCountryNames()
    {
        var names = Countries.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var alias in new[] { "USA", "U.S", "U.S.A", "United States of America" }) names[alias] = "US";
        foreach (var alias in new[] { "UK", "U.K", "Great Britain", "Britain" }) names[alias] = "GB";
        return names;
    }
}

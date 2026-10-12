using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Shapes a value for the field it goes into: NJ vs New Jersey, phone and date patterns, ZIP vs ZIP+4. Hints come
// from the field's label, help text, placeholder, and id. Never throws; when unsure, the value is left as it is.
// Formatting never makes a value longer than the field's max length: a shorter equivalent is used instead (the state
// code, the 5-digit ZIP, the phone's digits). When nothing fits, the value is left as it is, because cutting a name or
// a street would make it wrong; only a ZIP+4 can safely lose a part.
public static partial class ValueFormatter
{
    private static readonly Dictionary<string, string> Regions = BuildRegions();
    private static readonly Dictionary<string, string> RegionCodesByName =
        Regions.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Countries = BuildCountries();
    private static readonly Dictionary<string, string> CountryCodesByName = BuildCountryNames();

    // Every date a copied block may hold, for TryParseDate. Slashes read month first here: that is only right for
    // matching dates against each other, so formatting reads dates with ReadDate instead.
    private static readonly string[] DateInputFormats =
    [
        "M/d/yyyy", "M/d/yy", "M-d-yyyy", "yyyy-M-d", "yyyy/M/d", "d.M.yyyy", "MMMM d, yyyy", "MMMM d yyyy",
        "d MMMM yyyy", "d MMMM, yyyy", "MMM d, yyyy", "MMM d yyyy", "d MMM yyyy", "MMM. d, yyyy"
    ];

    // Dates whose day and month are certain: year first, month names, and day.month.year with dots.
    private static readonly string[] OrderedDateFormats =
    [
        "yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "d.M.yyyy", "d.M.yy", "MMMM d, yyyy", "MMMM d yyyy", "d MMMM yyyy",
        "d MMMM, yyyy", "MMM d, yyyy", "MMM d yyyy", "d MMM yyyy", "MMM. d, yyyy", "d MMM. yyyy"
    ];

    private enum DatePart { Year, Month, Day }

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
        int? max = field.MaxLength is > 0 ? field.MaxLength : null;
        try
        {
            return key switch
            {
                FieldKeys.Phone => FormatPhone(value, raw, words, max),
                FieldKeys.DateOfBirth => FormatDate(value, raw, words, field, max),
                FieldKeys.Zip => FormatZip(value, words, max),
                FieldKeys.State => FormatRegion(value, words, field, max),
                FieldKeys.Country => FormatCountry(value, words, field, max),
                _ => value
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException)
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

    // US and Canada (NANP) numbers take the field's pattern. Any other number keeps the way it was written, unless the
    // field asks for digits only or the international form. A NANP number has no "+" or "+1" before it, 10 digits (or
    // 11 starting with 1) in groups of 3, 3 and 4, and an area code and exchange that start with 2 to 9: so
    // "+45 12 34 56 78" and "138 1234 5678" are not reshaped into "(451) 234-5678" or "(381) 234-5678".
    private static string FormatPhone(string value, string raw, string words, int? max)
    {
        // An extension is not part of the number: a reshaped number leaves it out ("201-555-0100 ext. 12").
        var number = PhoneExtension().Replace(value, "").Trim();
        var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
        // Too few digits to be a phone number: nothing to reshape.
        if (digits.Length < 7) return value;
        var nanp = NanpNumber().Match(number);
        var local = nanp.Success ? nanp.Groups["area"].Value + nanp.Groups["exchange"].Value + nanp.Groups["line"].Value : null;
        var international = local is null && number.StartsWith('+');

        string? preferred = null;
        if (local is not null && PhoneMask().Match(raw) is { Success: true } mask) preferred = FillMask(mask.Value, local);
        else if (HasAny(words, "digits", "digits only", "numbers only", "numeric", "no dashes", "no spaces")) preferred = local ?? digits;
        else if (HasAny(words, "e 164", "international", "country code"))
            preferred = local is not null ? "+1" + local : international ? "+" + digits : null;

        // Shorter ways to write the same number, for a field too short for the others.
        string[] shorter = local is not null
            ? [$"({local[..3]}) {local[3..6]}-{local[6..]}", $"{local[..3]}-{local[3..6]}-{local[6..]}", local]
            : international ? ["+" + digits, digits] : [digits];
        return FirstThatFits(max, [preferred, value, number, .. shorter]) ?? value;
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

    private static string FormatDate(string value, string raw, string words, FieldContext field, int? max)
    {
        if (!ReadDate(value, out var date, out var orderKnown)) return value;
        // The box's own name, placeholder and id come first: a label shared by several boxes ("Date of birth
        // (MM/DD/YYYY)" over Month, Day and Year boxes) does not say which part this box holds.
        var own = string.Join(" | ", new[] { field.Name, field.Placeholder, LabelNormalizer.Humanize(field.AutomationId ?? "") }
            .Where(h => !string.IsNullOrWhiteSpace(h)));
        string? formatted = null;
        foreach (var hints in new[] { own, raw })
        {
            // A whole-date pattern wins over part words: "M/D/YYYY" is a date, not a year. One this cannot read
            // ("TT.MM.JJ") still means the whole date, which is then left as written.
            if (NamesWholeDate(hints))
            {
                // A date whose day and month are uncertain ("04/03/1990") is never reordered: it is pasted as written.
                formatted = orderKnown && DatePattern(hints) is { } format ? date.ToString(format, CultureInfo.InvariantCulture) : null;
                break;
            }
            if (SinglePart(LabelNormalizer.Normalize(hints)) is { } part)
            {
                formatted = PartOf(date, orderKnown, part, words, field, max);
                break;
            }
        }
        return FirstThatFits(max, [formatted, value]) ?? value;
    }

    // Hints with a pattern for a whole date: a mask with a month ("MM/YY", "TT.MM.JJ") or with three parts one of
    // which is a four-letter year ("gg.aa.yyyy"), or a pattern with the month's name ("Month DD, YYYY"). Letters that
    // are not one ("e.g.") do not count.
    private static bool NamesWholeDate(string hints) =>
        DateMask().Matches(hints).Any(mask =>
        {
            var tokens = mask.Groups["part"].Captures.Select(c => c.Value).ToArray();
            return tokens.Any(token => char.ToLowerInvariant(token[0]) == 'm') ||
                   (tokens.Length == 3 && tokens.Any(token => token.Length == 4));
        }) || MonthNamePattern().IsMatch(hints);

    // Reads a birth date and says whether its day and month are certain. "04/03/1990" may be 3 April or 4 March: both
    // parts are 12 or less, so the order is unknown. "13/04/1990", "1990-04-03" and "April 3, 1990" are certain. A
    // two-digit year goes in the century that puts the date in the past ("3/14/45" is 1945, not 2045).
    private static bool ReadDate(string value, out DateTime date, out bool orderKnown)
    {
        var text = Spaces().Replace(value.Trim(), " ");
        if (NumericDate().Match(text) is { Success: true } numeric)
        {
            var first = int.Parse(numeric.Groups["first"].Value, CultureInfo.InvariantCulture);
            var second = int.Parse(numeric.Groups["second"].Value, CultureInfo.InvariantCulture);
            var yearText = numeric.Groups["year"].Value;
            var year = int.Parse(yearText, CultureInfo.InvariantCulture);
            if (yearText.Length == 2) year = CultureInfo.InvariantCulture.Calendar.ToFourDigitYear(year);
            // Only the day can be over 12. When both parts could be either, month first is a reading, not a fact.
            var (month, day) = first > 12 ? (second, first) : (first, second);
            orderKnown = first > 12 || second > 12 || first == second;
            if (month is < 1 or > 12 || year is < 1 or > 9999 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                date = default;
                return false;
            }
            date = new DateTime(year, month, day);
            if (yearText.Length == 2) date = InThePast(date);
            return true;
        }

        orderKnown = true;
        foreach (var format in OrderedDateFormats)
        {
            if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) continue;
            if (!format.Contains("yyyy", StringComparison.Ordinal)) date = InThePast(date);
            return true;
        }
        date = default;
        return false;
    }

    private static DateTime InThePast(DateTime date) => date > DateTime.Today ? date.AddYears(-100) : date;

    // A box for one part of the date ("Year", "Birth month", "DD"). Null when the hints name several parts (a whole
    // date, "MM/DD/YYYY") or none.
    private static DatePart? SinglePart(string words)
    {
        var parts = new List<DatePart>(3);
        if (HasAny(words, "year", "yyyy", "yy", "aaaa", "jjjj", "yob")) parts.Add(DatePart.Year);
        if (HasAny(words, "month", "mm")) parts.Add(DatePart.Month);
        // "Birth day" is the whole birthday; "Day of birth" is the day.
        if ((Has(words, "day") && !Has(words, "birth day")) || Has(words, "dd")) parts.Add(DatePart.Day);
        return parts.Count == 1 ? parts[0] : null;
    }

    // One part of the date. Day and month need a date whose order is certain; the year never depends on it.
    private static string? PartOf(DateTime date, bool orderKnown, DatePart part, string words, FieldContext field, int? max)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (part)
        {
            case DatePart.Year:
                return max == 2 || (Has(words, "yy") && !Has(words, "yyyy")) ? date.ToString("yy", invariant) : date.ToString("yyyy", invariant);
            case DatePart.Month when orderKnown:
                // A drop-down list of months takes the name; a box ("MM") takes the number.
                var number = date.ToString("MM", invariant);
                return IsComboBox(field) && !Has(words, "mm") ? FirstThatFits(max, [date.ToString("MMMM", invariant), number]) : number;
            case DatePart.Day when orderKnown:
                return IsComboBox(field) && !Has(words, "dd") ? date.Day.ToString(invariant) : date.ToString("dd", invariant);
            default:
                return null;
        }
    }

    // A .NET format for the date pattern in the hints ("MM/DD/YYYY", "dd.mm.yyyy", "YYYY-MM-DD", "MM/YY",
    // "Month DD, YYYY"), or null when the hints name none. Other languages' letters work too: "dd/mm/aaaa" (Spanish),
    // "jj/mm/aaaa" (French), "TT.MM.JJJJ" (German).
    private static string? DatePattern(string raw)
    {
        foreach (Match mask in DateMask().Matches(raw))
            if (MaskFormat(mask) is { } format) return format;
        if (MonthNamePattern().Match(raw) is { Success: true } named)
            return named.Groups["dayFirst"].Success ? "d MMMM yyyy" : "MMMM d, yyyy";
        return null;
    }

    // "m" is the month, "d" the day, "y" the year, and any other letter written four times the year. One remaining
    // letter ("jj", "TT", "gg") is whichever part the pattern still lacks.
    private static string? MaskFormat(Match mask)
    {
        var tokens = mask.Groups["part"].Captures.Select(c => c.Value).ToArray();
        var kinds = tokens.Select(token => char.ToLowerInvariant(token[0]) switch
        {
            'm' => 'M',
            'd' => 'd',
            'y' => 'y',
            _ => token.Length == 4 ? 'y' : '?'
        }).ToArray();
        var unknown = Array.IndexOf(kinds, '?');
        if (unknown >= 0)
        {
            var missing = "Mdy".Where(kind => !kinds.Contains(kind)).ToArray();
            if (tokens.Length != 3 || kinds.Count(kind => kind == '?') != 1 || missing.Length != 1) return null;
            kinds[unknown] = missing[0];
        }
        // A usable pattern names the month, has no part twice, and has a day or a year.
        if (!kinds.Contains('M') || kinds.Distinct().Count() != kinds.Length || !(kinds.Contains('d') || kinds.Contains('y')))
            return null;
        // "MM DD YYYY" written with spaces most often means slashes.
        var separator = mask.Value.FirstOrDefault(c => c is '/' or '-' or '.');
        if (separator == default) separator = '/';
        var parts = tokens.Select((token, i) => kinds[i] switch
        {
            'y' => token.Length <= 2 ? "yy" : "yyyy",
            'M' => new string('M', Math.Min(token.Length, 4)),
            _ => token.Length == 1 ? "d" : "dd"
        });
        return string.Join($"'{separator}'", parts);
    }

    private static string FormatZip(string value, string words, int? max)
    {
        var text = value.Trim();
        var preferred = value;
        string? shorter = null;
        if (UsZip().Match(text) is { Success: true } us)
        {
            var five = us.Groups["five"].Value;
            var four = us.Groups["four"].Success ? us.Groups["four"].Value : null;
            // The second box of a split ZIP+4 takes only the 4-digit extension.
            if (four is not null && IsPlusFourBox(words, max)) return four;
            preferred = UsZipForm(value, five, four, words, max);
            shorter = five;
        }
        else if (CanadianPostal().Match(text) is { Success: true } ca)
        {
            var first = ca.Groups["first"].Value.ToUpperInvariant();
            var second = ca.Groups["second"].Value.ToUpperInvariant();
            preferred = max == 6 ? first + second : $"{first} {second}";
            shorter = first + second;
        }
        // A postal code reads the same without its spaces and dashes ("SW1A 1AA" -> "SW1A1AA").
        var compact = new string(text.Where(char.IsLetterOrDigit).ToArray());
        return FirstThatFits(max, [preferred, value, shorter, compact]) ?? value;
    }

    private static string UsZipForm(string value, string five, string? four, string words, int? max)
    {
        if (max == 5 || HasAny(words, "5 digit", "5 digits", "five digit")) return five;
        if (four is null) return value;
        if (max == 9) return five + four;
        if (max is >= 10 || HasAny(words, "zip 4", "zip plus 4", "plus 4", "9 digit")) return $"{five}-{four}";
        if (max is > 5 and < 9) return five;
        return value;
    }

    // The +4 box of a split ZIP: it holds at most 4 characters, or its label is only "+4" or "Plus 4".
    private static bool IsPlusFourBox(string words, int? max) =>
        max is <= 4 || (words.Length > 0 && words.Split(' ').All(word => word is "4" or "plus" or "four" or "ext" or "extension"));

    private static string FormatRegion(string value, string words, FieldContext field, int? max)
    {
        if (!TryRegion(value, out var code, out var name)) return value;
        var example = PlaceholderExample(field.Placeholder);
        string preferred;
        if (HasAny(words, "abbr", "abbreviation", "abbreviated", "code", "2 letter", "2 letters", "two letter") ||
            max is <= 3 || (example is { Length: 2 } && Regions.ContainsKey(example)))
            preferred = code;
        else if (HasAny(words, "full", "name", "spelled out") ||
                 (example is { Length: > 3 } && RegionCodesByName.ContainsKey(example)) ||
                 (IsComboBox(field) && max is null or > 3))
            preferred = name;
        else
            preferred = value;
        // The code is the shorter equivalent when the name does not fit.
        return FirstThatFits(max, [preferred, value, code]) ?? value;
    }

    private static string FormatCountry(string value, string words, FieldContext field, int? max)
    {
        if (!TryCountry(value, out var code, out var name)) return value;
        var example = PlaceholderExample(field.Placeholder);
        string preferred;
        if (HasAny(words, "code", "iso", "2 letter", "two letter") || max == 2 ||
            (example is { Length: 2 } && Countries.ContainsKey(example)))
            preferred = code;
        // Abbreviations ("US", "U.S.A.", "UK") read as names in a country field unless the field asks for a code.
        else if (IsComboBox(field) || HasAny(words, "name") || value.Count(char.IsLetter) <= 4)
            preferred = name;
        else
            preferred = value;
        return FirstThatFits(max, [preferred, value, code]) ?? value;
    }

    // The first form that fits the field; the first form at all when the field has no max length.
    private static string? FirstThatFits(int? max, IEnumerable<string?> forms) =>
        forms.FirstOrDefault(form => !string.IsNullOrEmpty(form) && (max is null || form.Length <= max));

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
    // "+1 (201) 555-0100", "1-201-555-0100", "201.555.0100", "2015550100".
    [GeneratedRegex(@"^(?:\+\s?1|1)?[\s.-]*(?:\(\s*(?<area>[2-9]\d{2})\s*\)|(?<area>[2-9]\d{2}))[\s.-]*(?<exchange>[2-9]\d{2})[\s.-]*(?<line>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex NanpNumber();
    // " ext. 12", " x12", " #12", ", extension 12" at the end of a phone number.
    [GeneratedRegex(@"\s*[,;]?\s*(?:ext\.?|extension|x|#)\s*\d{1,6}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhoneExtension();
    // Two or three runs of one repeated letter ("MM", "aaaa") joined by "/", ".", "-" or a space.
    [GeneratedRegex(@"(?<!\p{L})(?<part>(?<first>\p{L})\k<first>{0,3})(?:\s?[/.\- ]\s?(?<part>(?<next>\p{L})\k<next>{0,3})){1,2}(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateMask();
    [GeneratedRegex(@"(?<dayFirst>\b(?:dd?|day)\s+month\b)|\bmonth\s+(?:dd?|day)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthNamePattern();
    // "3/4/1990", "13-04-90": a numeric date with slashes or dashes, the year last.
    [GeneratedRegex(@"^(?<first>\d{1,2})(?<sep>[/-])(?<second>\d{1,2})\k<sep>(?<year>\d{4}|\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDate();
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

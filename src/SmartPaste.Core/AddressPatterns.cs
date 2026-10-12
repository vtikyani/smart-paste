using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// US and Canadian address shapes, shared by the local extractor and the contact-details check.
internal static partial class AddressPatterns
{
    private const int TimeoutMs = 200;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(TimeoutMs);

    // A unit: a designator, longest first so "Floor" is never read as "Fl" plus "oor", that ends there ("Flushing",
    // "Sterling", and "Aptos" are towns), then a unit number that holds a digit or is one or two characters ("Apt 4B",
    // "Unit A", "Ste PH"); or "#12".
    private const string UnitPattern =
        @"(?:(?i:Apartment|Building|Suite|Floor|Room|Unit|Bldg|Apt|Ste|Fl|Rm)\b\.?[ \t]*#?[ \t]*|#[ \t]*)" +
        @"(?:[A-Za-z]{0,3}-?\d[A-Za-z0-9-]{0,7}|[A-Za-z0-9]{1,2})(?![\p{L}\p{N}-])";

    // USPS street suffixes. "Center" makes "4 Embarcadero Center" the street, so the city after it is only the city.
    private const string StreetSuffix =
        "Street|Avenue|Boulevard|Parkway|Highway|Terrace|Crescent|Circle|Square|Center|Centre|Court|Drive|Place|Plaza|" +
        "Trail|Alley|Road|Lane|Loop|Way|Row|Blvd|Pkwy|Cres|Ave|Ctr|Hwy|Ter|Cir|Trl|Aly|Plz|St|Rd|Dr|Ln|Ct|Pl|Sq";

    // Canadian postal codes use only some letters, so few other "A1A 1A1" shapes qualify.
    private const string CanadianPostal = @"(?i:[ABCEGHJ-NPRSTVXY]\d[ABCEGHJ-NPRSTV-Z][ ]?\d[ABCEGHJ-NPRSTV-Z]\d)";

    // One to four capitalized words: "Springfield", "St. Louis", "Winston-Salem", "Rancho Santa Margarita".
    private const string CityPattern = @"(?<![\p{L}\p{N}'.-])(?<city>\p{Lu}[\p{L}'.-]{0,30}(?:[ \t]+\p{Lu}[\p{L}'.-]{0,30}){0,3})";

    // Words that make "Order 12345" or "Ticket K1A 0B1" something other than a city and its postal code.
    private static readonly HashSet<string> NotCityWords = new(StringComparer.Ordinal)
    {
        "order", "invoice", "ticket", "ref", "reference", "case", "item", "account", "acct", "total", "code", "zip",
        "postal", "postcode", "box", "tracking", "confirmation", "receipt", "transaction", "reservation", "booking",
        "customer", "member", "policy", "claim", "room", "suite", "unit", "apt", "floor", "phone", "tel", "fax", "id",
        "no", "number", "version", "page", "step", "year", "class"
    };

    // "Springfield, NJ 07081", "Springfield NJ 07081-1234", "Springfield, N.J. 07081", "St. Louis, Missouri 63101",
    // "Ottawa, ON K1A 0B1". State codes must be upper case ("in", "or", "me" are words); names may be any case.
    private static readonly Regex CityStateZipRegex = new(
        CityPattern + @"[ \t]*,?[ \t]+" +
        @"(?<state>[A-Z]\.?[A-Z]|(?i:" + string.Join('|', ValueFormatter.RegionNames.Select(Regex.Escape)) + @"))\.?[ \t]*,?[ \t]+" +
        @"(?<zip>\d{5}(?:-\d{4})?|" + CanadianPostal + @")(?![\w-])",
        RegexOptions.CultureInvariant, RegexTimeout);

    // State is null when the text names none ("Springfield 07081").
    public sealed record CityStateZip(string City, string? State, string Zip, int Index, int Length);

    public sealed record StreetMatch(string Street, string? Unit, int Index, int Length);

    // The first street (with its unit) and the first city, state, and postal code found in a run of lines.
    public sealed record Address(StreetMatch? Street, string? Unit, CityStateZip? Place);

    // The first address in the lines. A postal code counts only next to its city and state ("Springfield, NJ 07081"),
    // as a Canadian code after its city ("Ottawa K1A 0B1"), or as "Springfield 07081" on the street's line or the line
    // below it (below a unit line, if one comes first); never inside a link, an email address, or a house number.
    // streetKnown says a street was already found elsewhere (a labeled line), so a unit line counts from the start.
    public static Address Read(IReadOnlyList<string> lines, bool streetKnown = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        StreetMatch? street = null;
        string? unit = null;
        CityStateZip? place = null;
        var sawStreet = streetKnown;
        var belowStreet = false;
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = TextHeuristics.MaskLinks(raw);
            var start = 0;
            var onStreet = false;
            if (street is null && FindStreet(line) is { } found)
            {
                street = found;
                unit ??= found.Unit;
                start = found.Index + found.Length;
                onStreet = sawStreet = true;
            }
            else if (sawStreet && unit is null && UnitOnly(line) is { } unitLine)
            {
                // "Apt 4B" on a line of its own; the city may still come on the next line.
                unit = unitLine;
                continue;
            }
            place ??= FindCityStateZip(line, start) ?? FindCityZip(line, start, usZip: onStreet || belowStreet);
            belowStreet = onStreet;
        }
        return new Address(street, unit, place);
    }

    // The first city, state, and ZIP in the text at or after start, with a real state or province.
    public static CityStateZip? FindCityStateZip(string text, int start = 0)
    {
        for (var match = CityStateZipRegex.Match(text, start); match.Success; match = match.NextMatch())
        {
            var state = match.Groups["state"].Value;
            if (ValueFormatter.TryRegion(state, out _, out _))
                return new CityStateZip(match.Groups["city"].Value.Trim(), state, match.Groups["zip"].Value, match.Index, match.Length);
        }
        return null;
    }

    // A city and postal code with no state between: a Canadian code anywhere ("Ottawa K1A 0B1"), its shape is
    // distinctive; a US ZIP ("Springfield 07081") only when usZip says an address is expected here.
    public static CityStateZip? FindCityZip(string text, int start, bool usZip)
    {
        for (var match = CityZipRegex().Match(text, start); match.Success; match = match.NextMatch())
        {
            if (match.Groups["us"].Success && !usZip) continue;
            var city = match.Groups["city"].Value.Trim();
            if (LabelNormalizer.Normalize(city).Split(' ').Any(NotCityWords.Contains)) continue;
            return new CityStateZip(city, null, match.Groups["zip"].Value, match.Index, match.Length);
        }
        return null;
    }

    // The first street address, with the unit that directly follows or precedes it ("Apt 4B").
    public static StreetMatch? FindStreet(string text)
    {
        var match = StreetRegex().Match(text);
        if (!match.Success) return null;
        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.Trim()
            : match.Groups["lead"].Success ? match.Groups["lead"].Value.Trim() : null;
        return new StreetMatch(match.Groups["street"].Value.Trim(), unit, match.Index, match.Length);
    }

    // A line that holds only a unit: "Apt 4B", "Suite 200", "#12".
    public static string? UnitOnly(string line)
    {
        var match = UnitLineRegex().Match(line);
        return match.Success ? match.Groups["unit"].Value.Trim() : null;
    }

    // A labeled street value worth keeping: it has a house number or a street word ("Rose Cottage, Main Road"), unlike
    // "Same as billing" or "N/A".
    public static bool IsStreetLike(string value) => value.Any(char.IsAsciiDigit) || StreetWordRegex().IsMatch(value);

    // "123 Main St", "221B Baker St", "123 1/2 Main St", "PO Box 12", with a unit just before or after it ("Apt 4B 123
    // Main St", "123 Main St Apt 4B"). A street is a house number, one to four name words, and the first street suffix
    // after them, so in "123 Main St Crescent City" the town stays out. Name words start with a capital or a digit and
    // hold a letter ("5th"), so "12345 is on its way" and "Class of 2015 St. Mary's" are not addresses.
    [GeneratedRegex(
        @"(?:(?<lead>" + UnitPattern + @")[ \t]*,?[ \t]*)?" +
        @"(?<street>(?<![\p{L}\p{N}/.#-])\d{1,6}[A-Za-z]?(?:-\d{1,4})?(?:[ \t]*(?:1/2|½))?" +
        @"(?:[ \t]+(?=\d{0,6}\p{L})[\p{Lu}0-9][\p{L}0-9'.-]{0,30}){1,4}?" +
        @"[ \t]+(?=\p{Lu})(?i:" + StreetSuffix + @")\b\.?(?:[ \t]+(?:N|S|E|W|NE|NW|SE|SW)\b\.?)?" +
        @"|(?<![\p{L}\p{N}])(?i:P\.?[ ]?O\.?[ ]*Box)[ \t]+\d{1,10})" +
        @"(?:[ \t]*,?[ \t]*(?<unit>" + UnitPattern + @"))?",
        RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex StreetRegex();

    [GeneratedRegex(@"^[ \t]*(?<unit>" + UnitPattern + @")[ \t]*,?[ \t]*$", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex UnitLineRegex();

    [GeneratedRegex(CityPattern + @"[ \t]*,?[ \t]+(?<zip>(?<us>\d{5}(?:-\d{4})?)|(?<ca>" + CanadianPostal + @"))(?![\w-])",
        RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex CityZipRegex();

    [GeneratedRegex(@"\b(?i:" + StreetSuffix + @")\b", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex StreetWordRegex();
}

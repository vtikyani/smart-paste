using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// US and Canadian address shapes, shared by the local extractor and the multi-field check.
internal static partial class AddressPatterns
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    // "Springfield, NJ 07081", "Springfield NJ 07081-1234", "St. Louis, Missouri 63101", "Ottawa, ON K1A 0B1".
    // State codes must be upper case ("in", "or", "me" are words); names may be any case.
    private static readonly Regex CityStateZipRegex = new(
        @"(?<![\p{L}\p{N}'.-])(?<city>\p{Lu}[\p{L}'.-]*(?:[ \t]+\p{Lu}[\p{L}'.-]*){0,3})[ \t]*,?[ \t]+" +
        @"(?<state>[A-Z]{2}|(?i:" + string.Join('|', ValueFormatter.RegionNames.Select(Regex.Escape)) + @"))\.?[ \t]*,?[ \t]+" +
        @"(?<zip>\d{5}(?:-\d{4})?|[A-Za-z]\d[A-Za-z][ ]?\d[A-Za-z]\d)(?![\w-])",
        RegexOptions.CultureInvariant, RegexTimeout);

    public sealed record CityStateZip(string City, string State, string Zip, int Index, int Length);

    public sealed record StreetMatch(string Street, string? Unit, int Index, int Length);

    // The first city, state, and ZIP in the text at or after start, with a real state or province.
    public static CityStateZip? FindCityStateZip(string text, int start = 0)
    {
        for (var match = CityStateZipRegex.Match(text, start); match.Success; match = match.NextMatch())
        {
            var state = match.Groups["state"].Value;
            if (ValueFormatter.TryRegion(state, out _, out _))
                return new CityStateZip(match.Groups["city"].Value.Trim(), state, match.Groups["zip"].Value,
                    match.Index, match.Length);
        }
        return null;
    }

    // The first street address ("123 Main St", "PO Box 12"), with the unit that directly follows it ("Apt 4B").
    public static StreetMatch? FindStreet(string text)
    {
        var match = StreetRegex().Match(text);
        if (!match.Success) return null;
        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.Trim() : null;
        return new StreetMatch(match.Groups["street"].Value.Trim(), unit, match.Index, match.Length);
    }

    // A line that holds only a unit: "Apt 4B", "Suite 200", "#12".
    public static string? UnitOnly(string line)
    {
        var match = UnitLineRegex().Match(line);
        return match.Success ? match.Groups["unit"].Value.Trim() : null;
    }

    // Street words and the suffix start with a capital or a digit ("123 Main St", "5 5th AVE"), so prose such as
    // "12345 is on its way" is not an address.
    [GeneratedRegex(
        @"(?<street>\b\d{1,6}[A-Za-z]?(?:-\d{1,4})?[ \t]+(?:[\p{Lu}0-9][\p{L}0-9'.-]*[ \t]+){0,4}" +
        @"(?=\p{Lu})(?i:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Court|Ct|Way|Place|Pl|Parkway|Pkwy|Highway|Hwy|" +
        @"Terrace|Ter|Circle|Cir|Square|Sq|Trail|Trl|Loop|Row|Crescent|Cres|Alley|Aly|Plaza|Plz)\b\.?" +
        @"(?:[ \t]+(?:N|S|E|W|NE|NW|SE|SW)\b\.?)?|\b(?i:P\.?[ ]?O\.?[ ]+Box)[ \t]+\d+)" +
        @"(?:[ \t]*,?[ \t]*(?<unit>(?i:Apt|Apartment|Suite|Ste|Unit|Fl|Floor|Rm|Room|Bldg|Building)\.?[ \t]*#?[ \t]*[A-Za-z0-9-]+|#[ \t]*[A-Za-z0-9-]+))?",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex StreetRegex();

    [GeneratedRegex(
        @"^[ \t]*(?<unit>(?i:Apt|Apartment|Suite|Ste|Unit|Fl|Floor|Rm|Room|Bldg|Building)\.?[ \t]*#?[ \t]*[A-Za-z0-9-]+|#[ \t]*[A-Za-z0-9-]+)[ \t]*,?[ \t]*$",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex UnitLineRegex();
}

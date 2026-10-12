using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public static partial class ProfileNormalizer
{
    // Titles before a name ("Dr.", "Ms"), with or without the period.
    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "mx", "miss", "mister", "dr", "doctor", "prof", "professor", "sir", "dame", "rev",
        "reverend", "fr", "father", "hon", "lord", "lady", "capt", "captain"
    };

    // Suffixes after a name ("Jr.", "III", "PhD"), with or without the period.
    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "sr", "ii", "iii", "iv", "phd", "md", "dds", "dmd", "esq", "mba", "cpa", "rn", "jd", "llm", "mph", "pmp",
        "cfa", "obe", "mbe", "kc", "qc"
    };

    // Words that start a surname: "van der Berg", "de la Cruz", "bin Salman", "St. John".
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase)
    {
        "van", "von", "der", "den", "de", "del", "della", "dela", "la", "le", "da", "das", "dos", "du", "di", "bin",
        "binti", "bint", "ibn", "al", "el", "ter", "ten", "st", "ste", "saint", "sainte"
    };

    internal sealed record NameParts(string First, string? Middle, string Last);

    public static Entity Normalize(Entity e)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawKey, rawValue) in e.Fields)
        {
            var key = NormalizeKey(rawKey);
            var value = rawValue.Trim();
            if (key.Length > 0 && value.Length > 0)
                fields[key] = value;
        }

        // "Jordan Rivera (they/them)": a note in brackets or quotes is not part of the name a form asks for.
        if (fields.TryGetValue(FieldKeys.FullName, out var written) && WithoutAsides(written) is { Length: > 0 } plain)
            fields[FieldKeys.FullName] = plain;

        if (!fields.ContainsKey(FieldKeys.FullName) &&
            fields.TryGetValue(FieldKeys.FirstName, out var first) &&
            fields.TryGetValue(FieldKeys.LastName, out var last))
        {
            fields[FieldKeys.FullName] = fields.TryGetValue(FieldKeys.MiddleName, out var middle)
                ? $"{first} {middle} {last}" : $"{first} {last}";
        }

        if (!fields.ContainsKey(FieldKeys.FirstName) && !fields.ContainsKey(FieldKeys.LastName) &&
            fields.TryGetValue(FieldKeys.FullName, out var fullName) && SplitName(fullName) is { } name)
        {
            fields[FieldKeys.FirstName] = name.First;
            fields[FieldKeys.LastName] = name.Last;
            if (name.Middle is not null) fields.TryAdd(FieldKeys.MiddleName, name.Middle);
        }

        if (!fields.ContainsKey(FieldKeys.Linkedin) &&
            fields.TryGetValue(FieldKeys.Website, out var website) &&
            website.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase))
        {
            fields[FieldKeys.Linkedin] = website;
            fields.Remove(FieldKeys.Website);
        }

        return new Entity(e.Label.Trim(), fields);
    }

    // "First Name" or "first-name" -> "first_name".
    public static string NormalizeKey(string key) => KeySeparators().Replace(key.Trim().ToLowerInvariant(), "_");

    // A person's name split into first, middle, and last. Titles ("Dr."), suffixes ("Jr.", "PhD"), and anything in
    // brackets or quotes ("(they/them)") are left out, a surname keeps its particles ("Anna van der Berg"), and "Rivera,
    // Jordan" is read last name first. Null when it is not clearly a first and a last name.
    internal static NameParts? SplitName(string fullName)
    {
        var text = WithoutAsides(fullName).Trim(',', ' ');
        if (text.Length == 0) return null;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        // "Martin Luther King, Jr." and "Smith, Jane, PhD": a part that is only suffixes is not a name part.
        while (parts.Count > 1 && parts[^1].Split(' ').All(IsSuffix)) parts.RemoveAt(parts.Count - 1);
        if (parts.Count > 2) return null;

        if (parts.Count == 2)
        {
            // "Rivera, Jordan" or "van der Berg, Anna". With two words before the comma ("Jordan Rivera, Springfield")
            // the order is unclear, so nothing is derived.
            var family = NameWords(parts[0]);
            var given = NameWords(parts[1]);
            if (family is not { Count: >= 1 } || !family.SkipLast(1).All(IsParticle) || given is not { Count: >= 1 and <= 3 }) return null;
            return new NameParts(given[0], given.Count > 1 ? string.Join(' ', given.Skip(1)) : null, string.Join(' ', family));
        }

        var words = NameWords(parts[0]);
        if (words is null || words.Count < 2 || words.Count(word => !IsParticle(word)) > 4) return null;
        // The surname starts at the first particle that is not the last word ("van der Berg"), else it is the last word.
        var surname = words.Count - 1;
        for (var i = 1; i < words.Count - 1; i++)
        {
            if (!IsParticle(words[i])) continue;
            surname = i;
            break;
        }
        var middle = surname > 1 ? string.Join(' ', words.Skip(1).Take(surname - 1)) : null;
        return new NameParts(words[0], middle, string.Join(' ', words.Skip(surname)));
    }

    // The text without what is in brackets or quotes, single spaced: "Jordan Rivera (they/them)" -> "Jordan Rivera".
    internal static string WithoutAsides(string text) => Spaces().Replace(Asides().Replace(text, " "), " ").Trim();

    internal static bool IsHonorific(string word) => Honorifics.Contains(word.TrimEnd('.'));

    internal static bool IsSuffix(string word) => Suffixes.Contains(word.TrimEnd('.', ','));

    internal static bool IsParticle(string word) => Particles.Contains(word.TrimEnd('.'));

    // The words of a name without leading titles and trailing suffixes; null when a word is not part of a name (digits,
    // symbols) or there are too many words.
    private static List<string>? NameWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && IsHonorific(words[0])) words.RemoveAt(0);
        while (words.Count > 1 && IsSuffix(words[^1])) words.RemoveAt(words.Count - 1);
        return words.Count <= 6 && words.All(word => NameWord().IsMatch(word)) ? words : null;
    }

    [GeneratedRegex(@"[\s-]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex KeySeparators();

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]|""[^""]*""|“[^”]*”", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Asides();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Spaces();

    // Letters with apostrophes, hyphens, or periods: "Jordan", "O'Brien", "Mary-Jane", "J.", "JORDAN", "José".
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}'’.-]*$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NameWord();
}

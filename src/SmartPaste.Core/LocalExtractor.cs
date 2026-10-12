using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Reads a copied block into a profile on this PC, with no network, in milliseconds. It runs on every multi-field
// copy so Ctrl+V works at once; the parsing model's richer result replaces it when one arrives. Every value comes
// from the text itself; nothing is invented.
public static partial class LocalExtractor
{
    private const string Person = "person";

    // Words that make a capitalized first line a heading or a company rather than a person's name.
    private static readonly HashSet<string> NotNameWords = new(StringComparer.Ordinal)
    {
        "information", "info", "details", "contact", "contacts", "address", "addresses", "profile", "resume", "cv",
        "summary", "personal", "billing", "shipping", "mailing", "account", "customer", "order", "invoice", "receipt",
        "experience", "education", "skills", "references", "objective", "dear", "hello", "hi", "regards", "thanks",
        "inc", "llc", "ltd", "corp", "corporation", "company", "co", "group", "gmbh", "plc", "university", "college",
        "school", "bank", "hospital", "foundation", "associates", "partners", "services", "solutions", "technologies",
        "systems", "street", "avenue", "road", "suite", "apartment"
    };

    public static Profile Extract(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || text.Length > TextHeuristics.MaxChars) return new Profile([], now);
        try
        {
            return Read(text, now);
        }
        catch (RegexMatchTimeoutException)
        {
            // Text this convoluted is not a block of contact details.
            return new Profile([], now);
        }
    }

    private static Profile Read(string text, DateTimeOffset now)
    {
        var entities = new EntityBuilder();
        var lines = LabelLines.SplitLines(text);
        // Lines already read through their label, and lines that must not be read at all.
        var labeledLines = new HashSet<int>();
        var offLimits = new HashSet<int>();

        foreach (var line in LabelLines.Find(lines))
        {
            if (LabelLines.IsOffLimits(line.Label))
            {
                // "Emergency contact: Sam, 201-555-0199": that phone is not the user's, so the whole line is skipped.
                offLimits.Add(line.LineIndex);
                continue;
            }
            if (LabelLines.Map(line.Label) is not { } mapping) continue;
            labeledLines.Add(line.LineIndex);
            AddLabeled(entities.For(mapping.Entity ?? Person), mapping.Key, line.Value);
        }

        // Everything below reads free text, with off-limits lines blanked (line numbers stay the same).
        var free = lines.Select((line, index) => offLimits.Contains(index) ? "" : line).ToArray();
        var person = entities.For(Person);
        ReadAddress(free, labeledLines, person);
        if (!person.ContainsKey(FieldKeys.FullName) && !person.ContainsKey(FieldKeys.FirstName) &&
            NameFromFirstLine(free, labeledLines) is { } name)
            person[FieldKeys.FullName] = name;
        AddFastFields(TextHeuristics.Extract(string.Join('\n', free)), entities, person);

        return new Profile(entities.Build(), now);
    }

    private static void AddLabeled(Dictionary<string, string> fields, string key, string value)
    {
        if (fields.ContainsKey(key)) return;
        switch (key)
        {
            case FieldKeys.Street:
                // "Address: 123 Main St Apt 4B, Springfield NJ 07081" holds the whole address.
                if (!ReadAddressValue(value, fields)) fields[key] = value;
                break;
            case FieldKeys.Email:
                if (EmailRegex().Match(value) is { Success: true } email) fields[key] = email.Value;
                break;
            case FieldKeys.Phone:
                if (value.Count(char.IsAsciiDigit) >= 7) fields[key] = value;
                break;
            case FieldKeys.Zip:
                if (ZipRegex().Match(value) is { Success: true } zip) fields[key] = zip.Value;
                break;
            default:
                fields[key] = value;
                break;
        }
    }

    // Splits one address value into street, unit, city, state, and ZIP. False when it holds no street address.
    private static bool ReadAddressValue(string value, Dictionary<string, string> fields)
    {
        if (AddressPatterns.FindStreet(value) is not { } street) return false;
        fields.TryAdd(FieldKeys.Street, street.Street);
        if (street.Unit is not null) fields.TryAdd(FieldKeys.Unit, street.Unit);
        if (AddressPatterns.FindCityStateZip(value, street.Index + street.Length) is { } place) AddPlace(fields, place);
        return true;
    }

    // Street, unit, city, state, and ZIP from the unlabeled lines, whether on one line or several.
    private static void ReadAddress(IReadOnlyList<string> lines, HashSet<int> labeledLines, Dictionary<string, string> fields)
    {
        var sawStreet = fields.ContainsKey(FieldKeys.Street);
        for (var i = 0; i < lines.Count; i++)
        {
            if (labeledLines.Contains(i)) continue;
            var line = lines[i];
            var start = 0;
            if (!fields.ContainsKey(FieldKeys.Street) && AddressPatterns.FindStreet(line) is { } street)
            {
                fields[FieldKeys.Street] = street.Street;
                if (street.Unit is not null) fields.TryAdd(FieldKeys.Unit, street.Unit);
                start = street.Index + street.Length;
                sawStreet = true;
            }
            else if (sawStreet && !fields.ContainsKey(FieldKeys.Unit) && AddressPatterns.UnitOnly(line) is { } unit)
            {
                fields[FieldKeys.Unit] = unit;
                continue;
            }
            if (!fields.ContainsKey(FieldKeys.City) && AddressPatterns.FindCityStateZip(line, start) is { } place)
                AddPlace(fields, place);
        }
    }

    private static void AddPlace(Dictionary<string, string> fields, AddressPatterns.CityStateZip place)
    {
        fields.TryAdd(FieldKeys.City, place.City);
        if (place.State is not null) fields.TryAdd(FieldKeys.State, place.State);
        fields.TryAdd(FieldKeys.Zip, place.Zip);
    }

    // A conservative guess: the first line, or its first comma segment, when it is 2 to 4 capitalized words with
    // no digits that are not a heading, a label, a company, or a street.
    private static string? NameFromFirstLine(IReadOnlyList<string> lines, HashSet<int> labeledLines)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) { index = i; break; }
        if (index < 0 || labeledLines.Contains(index)) return null;

        var candidate = lines[index].Split(',')[0].Trim();
        var words = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 4 || !words.All(w => NameWord().IsMatch(w))) return null;
        var normalized = LabelNormalizer.Normalize(candidate);
        if (normalized.Split(' ').Any(NotNameWords.Contains)) return null;
        if (LabelCache.TryGetBuiltIn(normalized, out _) || KeywordRules.Match(normalized) is not null) return null;
        return string.Join(' ', words);
    }

    // Email, phone, ZIP, and links found anywhere, for keys the block has not filled yet.
    private static void AddFastFields(FastFields fast, EntityBuilder entities, Dictionary<string, string> person)
    {
        Fill(FieldKeys.Email, fast.Email);
        Fill(FieldKeys.Phone, fast.Phone);
        Fill(FieldKeys.Zip, fast.Zip);
        Fill(FieldKeys.Linkedin, fast.Linkedin);
        if (!string.Equals(fast.Website, fast.Linkedin, StringComparison.OrdinalIgnoreCase)) Fill(FieldKeys.Website, fast.Website);

        void Fill(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !entities.Has(key)) person[key] = value;
        }
    }

    // Entities in first-seen order, the person first.
    private sealed class EntityBuilder
    {
        private readonly List<(string Label, Dictionary<string, string> Fields)> _entities = [(Person, new(StringComparer.Ordinal))];

        public Dictionary<string, string> For(string label)
        {
            foreach (var (existing, fields) in _entities)
                if (existing.Equals(label, StringComparison.OrdinalIgnoreCase)) return fields;
            var created = new Dictionary<string, string>(StringComparer.Ordinal);
            _entities.Add((label, created));
            return created;
        }

        public bool Has(string key) => _entities.Any(e => e.Fields.ContainsKey(key));

        public IReadOnlyList<Entity> Build() => _entities
            .Select(e => ProfileNormalizer.Normalize(new Entity(e.Label, e.Fields)))
            .Where(e => e.Fields.Count > 0)
            .ToArray();
    }

    // "Jordan", "O'Brien", "McDonald", "Mary-Jane", or an initial such as "J.". All-caps words ("NJ") do not count.
    // Each repeated part starts with a mark or a capital, so matching never backtracks.
    [GeneratedRegex(@"^(?:\p{Lu}\p{Ll}*(?:['’-]\p{Lu}?\p{Ll}+|\p{Lu}\p{Ll}+)*|\p{Lu}\.)$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NameWord();

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{5}(?:-\d{4})?\b|\b[A-Za-z]\d[A-Za-z] ?\d[A-Za-z]\d\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ZipRegex();
}

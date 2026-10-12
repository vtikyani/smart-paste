using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Reads a copied block into a profile on this PC, with no network, in milliseconds. It runs on every multi-field
// copy so Ctrl+V works at once; the parsing model's richer result replaces it when one arrives. Every value comes
// from the text itself; nothing is invented, and a guess that is unsure (a name, a postal code) is left out.
public static partial class LocalExtractor
{
    private const string Person = "person";

    // Words that make a capitalized line a heading, a job title, or a company rather than a person's name.
    private static readonly HashSet<string> NotNameWords = new(StringComparer.Ordinal)
    {
        "information", "info", "details", "contact", "contacts", "address", "addresses", "profile", "resume", "cv",
        "summary", "personal", "billing", "shipping", "mailing", "delivery", "account", "customer", "order", "invoice",
        "receipt", "experience", "education", "skills", "references", "objective", "dear", "hello", "hi", "regards",
        "thanks", "attn", "attention", "recipient", "sender",
        "inc", "llc", "ltd", "corp", "corporation", "company", "co", "group", "gmbh", "plc", "university", "college",
        "school", "bank", "hospital", "foundation", "associates", "partners", "services", "solutions", "technologies",
        "systems", "street", "avenue", "road", "suite", "apartment",
        "engineer", "developer", "manager", "director", "designer", "analyst", "consultant", "specialist",
        "coordinator", "officer", "president", "executive", "administrator", "architect", "scientist", "intern",
        "founder", "representative", "recruiter", "technician", "supervisor", "programmer", "researcher", "assistant",
        "associate", "senior", "junior", "principal", "chief", "ceo", "cto", "cfo", "coo", "cio", "vp"
    };

    // Lines that only announce what follows ("Contact Information", "Resume", or a sign-off above a signature); the
    // name may be on the next line.
    private static readonly HashSet<string> Headings = new(StringComparer.Ordinal)
    {
        "contact", "contact information", "contact info", "contact details", "contact me", "personal information",
        "personal info", "personal details", "my details", "your details", "details", "profile", "resume", "cv",
        "curriculum vitae", "about me", "applicant", "applicant information", "candidate", "candidate information",
        "customer", "customer information", "customer details", "recipient", "address", "my address",
        "name and address", "attn", "attention", "to", "from", "sold to", "send to",
        "regards", "best regards", "kind regards", "warm regards", "best", "best wishes", "all the best", "thanks",
        "thank you", "many thanks", "cheers", "sincerely", "yours sincerely", "yours truly", "respectfully"
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
        // Card numbers, SSNs, and IBANs never become values, wherever they sit.
        var block = LabelLines.Read(Redaction.MaskNumbers(text, " "));
        var layout = Lay(block);
        var entities = new EntityBuilder();
        foreach (var labeled in block.Labeled)
        {
            var entity = labeled.Mapping.Entity ?? layout.EntityOf[labeled.Line.LineIndex] ?? Person;
            AddLabeled(entities.For(entity), labeled.Mapping.Key, labeled.Line.Value);
        }
        foreach (var section in layout.Sections) ReadSection(section, entities.For(section.Entity));
        return new Profile(entities.Build(), now);
    }

    // A run of lines that belong to one entity: the person's own lines, or a block under an address heading. A line's
    // text is null when it is labeled (its values were read through the label).
    private sealed record Section(string Entity, List<string?> Lines);

    // The entity each line belongs to (null for the person), and the sections in order.
    private sealed record Layout(string?[] EntityOf, List<Section> Sections);

    // A heading ("Shipping Address", "Bill to:") starts a block for that address, which ends at the next heading, or at
    // a blank line once it has content. An inline address label ("Billing address: 123 Main St") takes in only the
    // address lines right under it (a street, a unit, a city with its postal code), so an email address on the next
    // line stays the person's. Everything else is the person's.
    private static Layout Lay(LabelLines.Block block)
    {
        var entityOf = new string?[block.Free.Count];
        var sections = new List<Section>();
        Section? section = null;
        string? current = null;
        var inline = false;
        var hasContent = false;
        for (var i = 0; i < block.Free.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(block.Lines[i]))
            {
                if (current is not null && hasContent) (current, inline, section) = (null, false, null);
                continue;
            }
            // A line with an off-limits label is skipped, but it does not end a block.
            var line = block.Free[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (Heading(line) is { } heading)
            {
                current = heading.Entity;
                inline = block.IsLabeled(i);
                hasContent = heading.Rest.Length > 0;
                entityOf[i] = current;
                section = new Section(current, []);
                sections.Add(section);
                // "Ship to: Jordan Rivera" or "Shipping address: Jordan Rivera": the rest of the line is the block's
                // first line. When it is the address a label already gave, reading it again adds nothing.
                if (heading.Rest.Length > 0) section.Lines.Add(heading.Rest);
                continue;
            }
            if (inline && (block.IsLabeled(i) || !IsAddressLine(line))) (current, inline, section) = (null, false, null);
            entityOf[i] = current;
            var entity = current ?? Person;
            if (section is null || section.Entity != entity)
            {
                section = new Section(entity, []);
                sections.Add(section);
            }
            section.Lines.Add(block.IsLabeled(i) ? null : line);
            hasContent = true;
        }
        return new Layout(entityOf, sections);
    }

    // A line that carries an address on: a street, a unit, or a city with its postal code.
    private static bool IsAddressLine(string line)
    {
        var text = TextHeuristics.MaskLinks(line);
        return AddressPatterns.FindStreet(text) is not null || AddressPatterns.UnitOnly(text) is not null ||
               AddressPatterns.FindCityStateZip(text) is not null || AddressPatterns.FindCityZip(text, 0, usZip: true) is not null;
    }

    private static void AddLabeled(Dictionary<string, string> fields, string key, string value)
    {
        if (fields.ContainsKey(key)) return;
        switch (key)
        {
            case FieldKeys.Street:
                // "Address: 123 Main St Apt 4B, Springfield NJ 07081" holds the whole address.
                ReadAddressValue(value, fields);
                break;
            case FieldKeys.Email:
                if (TextHeuristics.FindEmail(value) is { } email) fields[key] = email;
                break;
            case FieldKeys.Phone:
                if (TextHeuristics.PhoneIn(value) is { } phone) fields[key] = phone;
                break;
            case FieldKeys.Zip:
                // A US or Canadian code inside the value, else a short value with a digit ("SW1A 2AA", "75008").
                if (ZipRegex().Match(value) is { Success: true } zip) fields[key] = zip.Value;
                else if (value.Length <= 10 && value.Any(char.IsAsciiDigit)) fields[key] = value;
                break;
            case FieldKeys.FirstName or FieldKeys.MiddleName or FieldKeys.LastName or FieldKeys.FullName:
                if (value.Any(char.IsLetter)) fields[key] = value;
                break;
            default:
                fields[key] = value;
                break;
        }
    }

    // Splits one address value into street, unit, city, state, and ZIP. A value with no street the patterns know is
    // kept as the street only when it looks like one ("Rose Cottage, Main Road"), never "Same as billing".
    private static void ReadAddressValue(string value, Dictionary<string, string> fields)
    {
        var address = AddressPatterns.Read([value]);
        if (address.Street is { } street)
        {
            fields.TryAdd(FieldKeys.Street, street.Street);
            if (address.Unit is { } unit) fields.TryAdd(FieldKeys.Unit, unit);
        }
        else if (address.Place is null && AddressPatterns.IsStreetLike(value))
        {
            fields.TryAdd(FieldKeys.Street, value);
        }
        if (address.Place is { } place) AddPlace(fields, place);
    }

    // The address, name, email, phone, and links in a section's unlabeled lines, for keys its entity does not have yet.
    private static void ReadSection(Section section, Dictionary<string, string> fields)
    {
        var lines = section.Lines.Select(line => line ?? "").ToArray();
        var labeledStreet = fields.ContainsKey(FieldKeys.Street);
        var address = AddressPatterns.Read(lines, labeledStreet);
        if (!labeledStreet && address.Street is { } street)
        {
            fields[FieldKeys.Street] = street.Street;
            if (address.Unit is { } unit) fields.TryAdd(FieldKeys.Unit, unit);
        }
        else if (labeledStreet && address.Street is null && address.Unit is { } unitLine)
        {
            // "Apt 4B" on its own line under a labeled street.
            fields.TryAdd(FieldKeys.Unit, unitLine);
        }
        if (address.Place is { } place) AddPlace(fields, place);

        if (!fields.ContainsKey(FieldKeys.FullName) && !fields.ContainsKey(FieldKeys.FirstName) && SectionName(section) is { } name)
            fields[FieldKeys.FullName] = name;

        var text = string.Join('\n', lines);
        var website = TextHeuristics.FindWebsite(text);
        var linkedin = TextHeuristics.FindLinkedin(text);
        Fill(FieldKeys.Email, TextHeuristics.FindEmail(text));
        Fill(FieldKeys.Phone, TextHeuristics.FindPhone(lines));
        Fill(FieldKeys.Linkedin, linkedin);
        if (!string.Equals(website, linkedin, StringComparison.OrdinalIgnoreCase)) Fill(FieldKeys.Website, website);

        void Fill(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.TryAdd(key, value);
        }
    }

    private static void AddPlace(Dictionary<string, string> fields, AddressPatterns.CityStateZip place)
    {
        fields.TryAdd(FieldKeys.City, place.City);
        if (place.State is not null) fields.TryAdd(FieldKeys.State, place.State);
        fields.TryAdd(FieldKeys.Zip, place.Zip);
    }

    // The name a section starts with: its first line, or the line after a heading such as "Contact Information". When
    // a labeled line comes first, the labels say what the section holds, so nothing is guessed.
    private static string? SectionName(Section section)
    {
        for (var i = 0; i < section.Lines.Count; i++)
        {
            var line = section.Lines[i];
            if (line is null) return null;
            if (i == 0 && Headings.Contains(LabelNormalizer.Normalize(line))) continue;
            return NameFrom(line);
        }
        return null;
    }

    // A conservative guess: two to four capitalized name words ("Jordan Rivera", "Siobhan J. O'Brien"), with an
    // optional title ("Dr."), suffix ("Jr."), surname particles ("van der Berg"), and note in brackets ("(they/them)"),
    // that are not a heading, a job title, a company, a label, or a street. Only the first comma segment is read, so
    // "Martin Luther King, Jr." gives "Martin Luther King".
    private static string? NameFrom(string line)
    {
        var candidate = ProfileNormalizer.WithoutAsides(line);
        var comma = candidate.IndexOf(',');
        if (comma >= 0) candidate = candidate[..comma];
        var words = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 7) return null;
        var core = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (i == 0 && words.Length > 1 && ProfileNormalizer.IsHonorific(word)) continue;
            if (i > 0 && i == words.Length - 1 && ProfileNormalizer.IsSuffix(word)) continue;
            if (i > 0 && i < words.Length - 1 && ProfileNormalizer.IsParticle(word)) continue;
            if (!NameWord().IsMatch(word)) return null;
            core++;
        }
        if (core is < 2 or > 4) return null;
        var normalized = LabelNormalizer.Normalize(candidate);
        if (Headings.Contains(normalized) || normalized.Split(' ').Any(NotNameWords.Contains)) return null;
        if (LabelCache.TryGetBuiltIn(normalized, out _) || KeywordRules.Match(normalized) is not null) return null;
        return string.Join(' ', words);
    }

    private sealed record HeadingLine(string Entity, string Rest);

    // "Shipping Address", "Billing Information:", "Ship to: Jordan Rivera", "Home address": a line that starts the
    // block for one address. Only "... to" and "... address" headings may carry the block's first line after a colon,
    // so "Shipping: Free" is not one.
    private static HeadingLine? Heading(string line)
    {
        var match = HeadingRegex().Match(line);
        if (!match.Success) return null;
        var rest = match.Groups["rest"].Value;
        var noun = match.Groups["noun"].Value.ToLowerInvariant().TrimEnd('.');
        if (rest.Length > 0 && noun is not ("address" or "addr" or "to")) return null;
        var entity = match.Groups["kind"].Value.ToLowerInvariant() switch
        {
            "shipping" or "ship" or "delivery" or "deliver" => "shipping address",
            "billing" or "bill" => "billing address",
            "mailing" => "mailing address",
            "home" => "home",
            _ => "work"
        };
        return new HeadingLine(entity, rest);
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

        public IReadOnlyList<Entity> Build() => _entities
            .Select(e => ProfileNormalizer.Normalize(new Entity(e.Label, e.Fields)))
            .Where(e => e.Fields.Count > 0)
            .ToArray();
    }

    // "Jordan", "O'Brien", "McDonald", "Mary-Jane", or an initial such as "J.". All-caps words ("NJ") do not count.
    // Each repeated part starts with a mark or a capital, so matching never backtracks.
    [GeneratedRegex(@"^(?:\p{Lu}\p{Ll}*(?:['’-]\p{Lu}?\p{Ll}+|\p{Lu}\p{Ll}+)*|\p{Lu}\.)$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NameWord();

    [GeneratedRegex(@"\b\d{5}(?:-\d{4})?\b|\b[A-Za-z]\d[A-Za-z] ?\d[A-Za-z]\d\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ZipRegex();

    [GeneratedRegex(
        @"^[ \t]*(?:(?<kind>shipping|billing|mailing|delivery)(?:[ \t]+(?<noun>address|addr\.?|information|info|details))?" +
        @"|(?<kind>home|work|office)[ \t]+(?<noun>address)|(?<kind>ship|bill|deliver)[ \t]+(?<noun>to))" +
        @"[ \t]*(?:[:：][ \t]*(?<rest>\S(?:.*\S)?)?)?[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex HeadingRegex();
}

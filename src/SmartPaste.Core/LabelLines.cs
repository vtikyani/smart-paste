using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// "Label: value" lines in a copied block ("Email: v@example.com", "Shipping city<TAB>Springfield"), mapped to field
// keys with the built-in label list and keyword rules.
internal static partial class LabelLines
{
    // Label prefixes that put a value in its own entity ("Shipping city" belongs to the shipping address).
    private static readonly Dictionary<string, string> EntityPrefixes = new(StringComparer.Ordinal)
    {
        ["billing"] = "billing address",
        ["shipping"] = "shipping address",
        ["delivery"] = "shipping address",
        ["mailing"] = "mailing address",
        ["home"] = "home",
        ["work"] = "work"
    };

    public sealed record Line(string Label, string Value, int LineIndex);

    // A label's field key, and the entity its prefix names (null for the person's own details).
    public sealed record Mapping(string Key, string? Entity);

    // A labeled value the block holds for a known field.
    public sealed record Labeled(Line Line, Mapping Mapping);

    // A whole line, or one part of a line split at " | ". Label and Value are set when the part reads "Label: value".
    public sealed record Segment(string Text, string? Label, string? Value);

    // A copied block ready to read: its lines, the same lines as free text with every line that has an off-limits label
    // blanked (line numbers stay the same), and the values it labels for known fields.
    public sealed class Block
    {
        private readonly HashSet<int> _labeledLines;

        public Block(IReadOnlyList<string> lines, IReadOnlyList<string> free, IReadOnlyList<Labeled> labeled)
        {
            Lines = lines;
            Free = free;
            Labeled = labeled;
            _labeledLines = labeled.Select(l => l.Line.LineIndex).ToHashSet();
        }

        public IReadOnlyList<string> Lines { get; }
        public IReadOnlyList<string> Free { get; }
        public IReadOnlyList<Labeled> Labeled { get; }

        // True when the line holds a known label, so its value was read through that label.
        public bool IsLabeled(int lineIndex) => _labeledLines.Contains(lineIndex);
    }

    public static Block Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = SplitLines(text);
        var offLimits = new HashSet<int>();
        var labeled = new List<Labeled>();
        foreach (var line in Find(lines))
        {
            if (IsOffLimits(line.Label))
            {
                // "Emergency contact: Sam, 201-555-0199": that phone is not the user's, so the whole line is skipped.
                offLimits.Add(line.LineIndex);
                continue;
            }
            if (Map(line.Label) is { } mapping) labeled.Add(new Labeled(line, mapping));
        }
        var free = lines.Select((line, index) => offLimits.Contains(index) ? "" : line).ToArray();
        return new Block(lines, free, labeled);
    }

    public static IReadOnlyList<string> SplitLines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    public static IEnumerable<Line> Find(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            foreach (var segment in Segments(lines[i]))
                if (segment.Label is not null) yield return new Line(segment.Label, segment.Value!, i);
    }

    // "Name: Jordan | Email: v@example.com" holds two labeled values.
    public static IReadOnlyList<Segment> Segments(string line)
    {
        var parts = Separators().Split(line);
        var segments = new Segment[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var match = LabeledRegex().Match(parts[i]);
            segments[i] = match.Success
                ? new Segment(parts[i], match.Groups["label"].Value.Trim(), match.Groups["value"].Value)
                : new Segment(parts[i], null, null);
        }
        return segments;
    }

    public static bool HasLabel(string line) => Segments(line).Any(segment => segment.Label is not null);

    // Labels whose values must never be read as the user's details: sensitive ones (password, card number, bank
    // account) and ones about someone else (emergency contact, reference).
    public static bool IsOffLimits(string label)
    {
        var normalized = LabelNormalizer.Normalize(label);
        return Redaction.IsSensitiveLabel(normalized) || KeywordRules.IsAboutSomeoneElse(normalized);
    }

    // Null for labels that are not recognized or are off limits.
    public static Mapping? Map(string label)
    {
        var normalized = LabelNormalizer.Normalize(label);
        if (normalized.Length == 0 || IsOffLimits(normalized)) return null;
        string? entity = null;
        string? rest = null;
        var space = normalized.IndexOf(' ');
        if (space > 0 && EntityPrefixes.TryGetValue(normalized[..space], out var prefixEntity))
        {
            entity = prefixEntity;
            rest = normalized[(space + 1)..];
        }
        var key = Lookup(normalized) ?? (rest is null ? null : Lookup(rest));
        return key is null ? null : new Mapping(key, entity);
    }

    private static string? Lookup(string label) =>
        LabelCache.TryGetBuiltIn(label, out var key) ? key : KeywordRules.Match(label);

    // A label is short, starts with a letter (or a digit and a letter, as in "2FA code"), and is followed by a colon or a
    // tab and a value. The value ends at its last non-space character, found without backtracking over long runs.
    [GeneratedRegex(@"^[ \t]*(?<label>(?:\p{L}|\d\p{L})[\p{L}\p{N} .'’/&()#*-]{0,39}?)[ \t]*(?::|：|\t)[ \t]*(?<value>\S(?:.*\S)?)[ \t]*$",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex LabeledRegex();

    [GeneratedRegex(@"[ \t]+[|•·][ \t]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Separators();
}

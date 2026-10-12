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

    // A whole line, or one part of it: split at " | ", and before every later label it holds. Label and Value are set
    // when the part reads "Label: value" or "Label<TAB>value"; ByTab when a tab, not a colon, follows the label.
    public sealed record Segment(string Text, string? Label, string? Value, bool ByTab = false);

    // Words that can make up a label, at most this many, before the colon or tab that ends it.
    private const int MaxLabelWords = 4;

    // Separators looked at per part: a contact detail line has a few, and nothing gets slow on a pathological one.
    private const int MaxSeparatorsPerPart = 64;

    // What a piece of text left before a later label may end with: the space and punctuation between two values.
    private static readonly char[] PieceEnd = [' ', '\t', ',', ';', '/', '-', '–', '—'];

    private enum LabelKind { None, BuiltIn, Other }

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

    // "Name: Jordan | Email: v@example.com" holds two labeled values, and so do "Email: v@example.com Password: x" and
    // "Name<TAB>Jordan<TAB>Phone<TAB>201-555-0100": each value is read, and each secret removed, under its own label.
    public static IReadOnlyList<Segment> Segments(string line)
    {
        var segments = new List<Segment>();
        Dictionary<string, LabelKind>? kinds = null;
        foreach (var part in Separators().Split(line))
            foreach (var piece in SplitBeforeLaterLabels(part, kinds ??= new Dictionary<string, LabelKind>(StringComparer.Ordinal)))
            {
                var match = LabeledRegex().Match(piece);
                segments.Add(match.Success
                    ? new Segment(piece, match.Groups["label"].Value.Trim(), match.Groups["value"].Value, match.Groups["sep"].Value == "\t")
                    : new Segment(piece, null, null));
            }
        return segments;
    }

    public static bool HasLabel(string line) => Segments(line).Any(segment => segment.Label is not null);

    // True when a part of the line reads "Label: value" with a colon. A tab cell does not count: under a lone label, a
    // row of a copied table is that label's value whatever its first cell says.
    public static bool HasColonLabel(string line) => Segments(line).Any(segment => segment.Label is not null && !segment.ByTab);

    // A part, cut before each later label it holds. The first label of the part stays with it.
    private static IEnumerable<string> SplitBeforeLaterLabels(string part, Dictionary<string, LabelKind> kinds)
    {
        var position = 0;
        foreach (var start in LaterLabelStarts(part, kinds))
        {
            var piece = part[position..start].TrimEnd(PieceEnd);
            if (piece.Length > 0) yield return piece;
            position = start;
        }
        yield return part[position..];
    }

    // Where later labels start in a part. A label is the words right before a colon (not a link's "://", nor a time,
    // whose words start with a digit) or a whole tab cell, when they read as a label: the longest run of words that is
    // a built-in label, or else the shortest that is a secret's label, one about someone else, or one the keyword rules
    // know (so a name in front stays with its value). A run that reaches back to the start of the part is the part's
    // own first label.
    private static List<int> LaterLabelStarts(string part, Dictionary<string, LabelKind> kinds)
    {
        var starts = new List<int>();
        var first = 0;
        while (first < part.Length && char.IsWhiteSpace(part[first])) first++;
        var examined = 0;
        for (var i = first + 1; i < part.Length && examined < MaxSeparatorsPerPart; i++)
        {
            var c = part[i];
            int? start;
            if (c == '\t')
            {
                examined++;
                start = CellLabelStart(part, i, kinds);
            }
            else if (c is ':' or '：' && (i + 1 >= part.Length || part[i + 1] != '/'))
            {
                examined++;
                start = ColonLabelStart(part, i, kinds);
            }
            else
            {
                continue;
            }
            if (start is { } s && s > first && (starts.Count == 0 || s > starts[^1])) starts.Add(s);
        }
        return starts;
    }

    // The label that ends at the colon at index end, by the rules above, or null.
    private static int? ColonLabelStart(string part, int end, Dictionary<string, LabelKind> kinds)
    {
        var words = WordStartsBefore(part, end);
        if (words.Count == 0 || words[^1].ReachesStart) return null;
        int? builtIn = null, other = null;
        foreach (var (start, _) in words)
        {
            var kind = KindOf(part[start..end], kinds);
            if (kind == LabelKind.BuiltIn) builtIn = start;
            else if (kind == LabelKind.Other) other ??= start;
        }
        return builtIn ?? other;
    }

    // The label that fills the tab cell ending at index end, or null.
    private static int? CellLabelStart(string part, int end, Dictionary<string, LabelKind> kinds)
    {
        var cellStart = part.LastIndexOf('\t', end - 1) + 1;
        while (cellStart < end && part[cellStart] == ' ') cellStart++;
        if (cellStart >= end || !StartsLikeALabel(part, cellStart)) return null;
        return KindOf(part[cellStart..end], kinds) == LabelKind.None ? null : cellStart;
    }

    // Up to MaxLabelWords words right before end, nearest first, separated by spaces only: a tab, or a word glued to
    // something that is not a label character ("v@example.com"), ends the run. ReachesStart tells whether the run goes
    // back to the start of the text.
    private static List<(int Start, bool ReachesStart)> WordStartsBefore(string text, int end)
    {
        var words = new List<(int Start, bool ReachesStart)>();
        var i = end - 1;
        while (words.Count < MaxLabelWords)
        {
            while (i >= 0 && text[i] == ' ') i--;
            if (i < 0 || !IsLabelChar(text[i])) break;
            while (i >= 0 && IsLabelChar(text[i])) i--;
            var start = i + 1;
            if (!StartsLikeALabel(text, start) || (i >= 0 && text[i] is not (' ' or '\t'))) break;
            var before = i;
            while (before >= 0 && text[before] == ' ') before--;
            words.Add((start, before < 0));
            if (i >= 0 && text[i] == '\t') break;
        }
        return words;
    }

    private static LabelKind KindOf(string words, Dictionary<string, LabelKind> kinds)
    {
        if (kinds.TryGetValue(words, out var kind)) return kind;
        var normalized = LabelNormalizer.Normalize(words);
        kind = normalized.Length is 0 or > 40 ? LabelKind.None
            : LabelCache.TryGetBuiltIn(normalized, out _) ? LabelKind.BuiltIn
            : Redaction.IsSensitiveLabel(normalized) || KeywordRules.IsAboutSomeoneElse(normalized) || Map(normalized) is not null
                ? LabelKind.Other
                : LabelKind.None;
        kinds[words] = kind;
        return kind;
    }

    // The characters a label is made of, as in LabeledRegex.
    private static bool IsLabelChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '\'' or '’' or '/' or '&' or '(' or ')' or '#' or '*' or '-';

    // A label starts with a letter, or a digit and a letter ("2FA code").
    private static bool StartsLikeALabel(string text, int start) =>
        start < text.Length && (char.IsLetter(text[start]) ||
                                (char.IsDigit(text[start]) && start + 1 < text.Length && char.IsLetter(text[start + 1])));

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

    // A label is short, starts with a letter (or a digit and a letter, as in "2FA code"), and is followed by a colon or
    // a tab and a value. The value ends at its last non-space character, found without backtracking over long runs.
    [GeneratedRegex(@"^[ \t]*(?<label>(?:\p{L}|\d\p{L})[\p{L}\p{N} .'’/&()#*-]{0,39}?)[ \t]*(?<sep>:|：|\t)[ \t]*(?<value>\S(?:.*\S)?)[ \t]*$",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex LabeledRegex();

    [GeneratedRegex(@"[ \t]+[|•·][ \t]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Separators();
}

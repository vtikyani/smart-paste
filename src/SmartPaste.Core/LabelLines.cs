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
        ["mailing"] = "mailing address",
        ["home"] = "home",
        ["work"] = "work"
    };

    public sealed record Line(string Label, string Value, int LineIndex);

    // A label's field key, and the entity its prefix names (null for the person's own details).
    public sealed record Mapping(string Key, string? Entity);

    public static IReadOnlyList<string> SplitLines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    public static IEnumerable<Line> Find(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            // "Name: Jordan | Email: v@example.com" holds two labeled values.
            foreach (var segment in Separators().Split(lines[i]))
            {
                var match = LabeledRegex().Match(segment);
                if (match.Success) yield return new Line(match.Groups["label"].Value.Trim(), match.Groups["value"].Value.Trim(), i);
            }
        }
    }

    // Null for labels that are not recognized, are sensitive, or are about someone else (emergency contact).
    public static Mapping? Map(string label)
    {
        var normalized = LabelNormalizer.Normalize(label);
        if (normalized.Length == 0 || KeywordRules.IsSensitive(normalized) || KeywordRules.IsAboutSomeoneElse(normalized))
            return null;
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

    // How many different fields the block labels ("Name:", "Email:", "City:"...).
    public static int CountRecognized(string text)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Find(SplitLines(text)))
            if (Map(line.Label) is { } mapping) keys.Add(mapping.Key);
        return keys.Count;
    }

    private static string? Lookup(string label) =>
        LabelCache.TryGetBuiltIn(label, out var key) ? key : KeywordRules.Match(label);

    // A label starts with a letter, is short, and is followed by a colon or a tab and a value.
    [GeneratedRegex(@"^[ \t]*(?<label>\p{L}[\p{L}\p{N} .'/&()#*-]{0,39}?)[ \t]*(?::|：|\t)[ \t]*(?<value>\S.*?)[ \t]*$",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex LabeledRegex();

    [GeneratedRegex(@"[ \t]+[|•·][ \t]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Separators();
}

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SmartPaste.Core;

// One answer Jev can give: a distinct value from the armed block (Candidate holds the raw value), or "none".
public sealed record JevOption(string Name, JsonNode Description, Candidate? Candidate)
{
    public bool IsNone => Candidate is null;

    // The description as the plain string sent to Jev. A string is the one criteria shape every route documents, so
    // any other node is sent as its JSON text.
    public string DescriptionText =>
        Description is JsonValue value && value.TryGetValue<string>(out var text) ? text : Description.ToJsonString();
}

// What Jev is asked about one field: the field as state, plain instructions, and one option per distinct value of
// the armed block, with "none" last (Jev leans toward options listed first).
public sealed class JevQuestion
{
    public const string NoneOption = "none";

    // TypeSafe's limit for one choice question.
    public const int MaxOptions = 255;

    // Values are trimmed before they leave the PC: enough for Jev to tell them apart, no more.
    public const int ValueLimit = 60;

    // Option names stay short: the key part, and the entity part for a key with several values.
    private const int KeyNameLimit = 40;
    private const int EntityNameLimit = 24;
    private const int NameLimit = 100;

    public const string FieldInstructions =
        "The user copied a block of their own details, then pressed paste in the form field described in the state. " +
        "Which value from the block does that field ask for? Pick the option whose field matches what the form field asks " +
        "for, and when several values share a field, the one whose owner (in parentheses) matches the field's label or " +
        "sections. Pick none when the field asks for something the block does not contain, or for another person's " +
        "details, such as an emergency contact, a reference, a manager, or a recruiter.";

    private const string NoneDescription = "None of these values belongs in this field";

    private readonly Dictionary<string, JevOption> _byName;

    public JevQuestion(JsonObject state, string instructions, IReadOnlyList<JevOption> options)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count is 0 or > MaxOptions)
            throw new ArgumentOutOfRangeException(nameof(options), $"A choice needs 1 to {MaxOptions} options.");
        _byName = new Dictionary<string, JevOption>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            if (!IsValidName(option.Name))
                throw new ArgumentException("Option names use only a-z, 0-9 and '_' (at most 100 characters).", nameof(options));
            if (!_byName.TryAdd(option.Name, option))
                throw new ArgumentException("Option names must be unique.", nameof(options));
        }
        State = state;
        Instructions = instructions;
        Options = options;
    }

    public JsonObject State { get; }
    public string Instructions { get; }
    public IReadOnlyList<JevOption> Options { get; }
    public IReadOnlyCollection<string> OptionNames => _byName.Keys;

    // The option an answer names. Names are lowercase, so an answer that differs only in case or surrounding spaces
    // still maps back to its option.
    public JevOption? Option(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _byName.GetValueOrDefault(name) ?? _byName.GetValueOrDefault(name.Trim().ToLowerInvariant());
    }

    // The question for a field: the field's context as state and every distinct value of the profile as an option.
    public static JevQuestion ForField(FieldContext field, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(profile);
        return new JevQuestion(field.ToJevState(), FieldInstructions, BuildOptions(profile));
    }

    // One option per distinct value (per key), standard keys first, then "none". Names use only a-z, 0-9 and "_", a
    // shape every route keeps intact: a key with one distinct value is named after the key ("first_name"); otherwise
    // the entity follows two underscores ("street__shipping_address"), with a number when that is still taken. Names
    // are stable for the same profile, so answers map back to values.
    public static IReadOnlyList<JevOption> BuildOptions(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var options = new List<JevOption>();
        var used = new HashSet<string>(StringComparer.Ordinal) { NoneOption };
        var severalEntities = profile.Entities.Count > 1;
        // One slot stays free for "none".
        var room = MaxOptions - 1;
        foreach (var key in profile.Keys)
        {
            var values = DistinctValues(profile, key);
            var keyName = Slug(key, KeyNameLimit, "value");
            foreach (var (entity, value) in values.Take(room - options.Count))
            {
                var name = Unique(values.Count == 1 ? keyName : $"{keyName}__{Slug(entity.Label, EntityNameLimit, "item")}", used);
                var owner = severalEntities || values.Count > 1 ? entity.Label : null;
                options.Add(new JevOption(name, JsonValue.Create(Describe(key, owner, value)), new Candidate(key, entity.Label, value)));
            }
        }
        options.Add(new JevOption(NoneOption, JsonValue.Create(NoneDescription), null));
        return options;
    }

    // The request body for one route. The state is cloned so the same question can be sent to a second route.
    internal JsonObject ToRequestBody(string model, string questionId)
    {
        var criteria = new JsonObject();
        foreach (var option in Options) criteria[option.Name] = option.DescriptionText;
        return new JsonObject
        {
            ["model"] = model,
            ["state"] = State.DeepClone(),
            ["questions"] = new JsonObject
            {
                [questionId] = new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = Instructions,
                    ["criteria"] = criteria
                }
            }
        };
    }

    // Values that differ only in case or spacing are the same value: one option, first entity wins.
    internal static IReadOnlyList<(Entity Entity, string Value)> DistinctValues(Profile profile, string key)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<(Entity, string)>();
        foreach (var (entity, value) in profile.ValuesFor(key))
        {
            var collapsed = Collapse(value);
            if (collapsed.Length > 0 && seen.Add(collapsed)) values.Add((entity, value));
        }
        return values;
    }

    // "Street (shipping address): 2 Oak Ave — Street address: house number and street (address line 1)". The
    // definition after the dash tells neighbours apart (first name vs full name); it is left out when it would only
    // repeat the name, as for extra keys.
    private static string Describe(string key, string? owner, string value)
    {
        var name = FieldKeys.DisplayName(key);
        var text = new StringBuilder(name);
        if (owner is not null && Cut(owner, ValueLimit) is { Length: > 0 } entity) text.Append(" (").Append(entity).Append(')');
        text.Append(": ").Append(Cut(value, ValueLimit));
        var meaning = FieldKeys.Describe(key);
        if (!string.Equals(meaning, name, StringComparison.OrdinalIgnoreCase)) text.Append(" — ").Append(meaning);
        return text.ToString();
    }

    private static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= NameLimit && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{name}_{n}";
        return candidate;
    }

    // "Shipping address" -> "shipping_address", "número" -> "numero", "work_phone_#" -> "work_phone": lowercase ASCII
    // letters and digits joined by single underscores, at most limit characters, or fallback when nothing is left.
    private static string Slug(string text, int limit, string fallback)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in WithoutAccents(text))
        {
            var lower = char.ToLowerInvariant(c);
            if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower)) builder.Append(lower);
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }
        var slug = builder.ToString().Trim('_');
        if (slug.Length > limit) slug = slug[..limit].TrimEnd('_');
        return slug.Length == 0 ? fallback : slug;
    }

    // "número" -> "numero": accents are split off and dropped. Text that is not valid Unicode is kept as it is.
    private static string WithoutAccents(string text)
    {
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            return text;
        }
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString();
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Cut(string value, int length)
    {
        var text = Collapse(value);
        return text.Length <= length ? text : text[..length].TrimEnd();
    }
}

using System.Text;
using System.Text.Json.Nodes;

namespace SmartPaste.Core;

// One answer Jev can give: a distinct value from the armed block (Candidate holds the raw value), or "none".
public sealed record JevOption(string Name, JsonNode Description, Candidate? Candidate)
{
    public bool IsNone => Candidate is null;
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

    public const string FieldInstructions =
        "The user copied a block of their own details, then pressed paste in the form field described in the state. " +
        "Which value from the block does that field ask for? Pick the option whose field matches what the form field asks " +
        "for, and when several values share a field, the one whose belongs_to matches the field's label or sections. " +
        "Pick none when the field asks for something the block does not contain, or for another person's details, " +
        "such as an emergency contact, a reference, a manager, or a recruiter.";

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
            if (!_byName.TryAdd(option.Name, option))
                throw new ArgumentException("Option names must be unique.", nameof(options));
        State = state;
        Instructions = instructions;
        Options = options;
    }

    public JsonObject State { get; }
    public string Instructions { get; }
    public IReadOnlyList<JevOption> Options { get; }
    public IReadOnlyCollection<string> OptionNames => _byName.Keys;

    public JevOption? Option(string name) => _byName.GetValueOrDefault(name);

    // The question for a field: the field's context as state and every distinct value of the profile as an option.
    public static JevQuestion ForField(FieldContext field, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(profile);
        return new JevQuestion(field.ToJevState(), FieldInstructions, BuildOptions(profile));
    }

    // One option per distinct value (per key), standard keys first, then "none". A key with one distinct value is
    // named after the key ("first_name"); otherwise the entity is added ("street.shipping_address"), with a number
    // when that is still taken. Names are stable for the same profile, so answers map back to values.
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
            foreach (var (entity, value) in values.Take(room - options.Count))
            {
                var name = Unique(values.Count == 1 ? key : $"{key}.{Slug(entity.Label)}", used);
                var description = new JsonObject
                {
                    ["field"] = FieldKeys.Describe(key),
                    ["value"] = Cut(value, ValueLimit)
                };
                if (severalEntities || values.Count > 1) description["belongs_to"] = Cut(entity.Label, ValueLimit);
                options.Add(new JevOption(name, description, new Candidate(key, entity.Label, value)));
            }
        }
        options.Add(new JevOption(NoneOption, JsonValue.Create(NoneDescription), null));
        return options;
    }

    // The request body for one route. Nodes are cloned so the same question can be sent to a second route.
    internal JsonObject ToRequestBody(string model, string questionId)
    {
        var criteria = new JsonObject();
        foreach (var option in Options) criteria[option.Name] = option.Description.DeepClone();
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

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{name}_{n}";
        return candidate;
    }

    // "Shipping address" -> "shipping_address".
    private static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }
        var slug = builder.ToString().Trim('_');
        if (slug.Length > 24) slug = slug[..24].TrimEnd('_');
        return slug.Length == 0 ? "item" : slug;
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Cut(string value, int length)
    {
        var text = Collapse(value);
        return text.Length <= length ? text : text[..length].TrimEnd();
    }
}

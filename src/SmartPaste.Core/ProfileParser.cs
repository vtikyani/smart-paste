using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public static class ProfileParser
{
    // Reads the parsing model's reply: {"entities":[{"label":"person","fields":...}]}. Fields may be an object
    // ({"first_name":"Ada"}) or a list of pairs ([{"key":"first_name","value":"Ada"}], the structured-output shape).
    // Prose or code fences around the JSON are ignored.
    public static Profile Parse(string llmContent, DateTimeOffset now) => Build(ParseEntities(llmContent), now);

    // Like Parse, but values that do not appear in the source text are dropped (see ProfileGrounding). Grounding runs
    // before names are derived, so a full name built from a grounded first and last name stays. Like every profile
    // built here, it never holds a secret (see WithoutSecrets).
    public static Profile ParseGrounded(string llmContent, string sourceText, DateTimeOffset now)
    {
        var grounding = new ProfileGrounding(sourceText);
        return Build(ParseEntities(llmContent).Select(grounding.Ground), now);
    }

    public static Profile FromFastFields(FastFields f, DateTimeOffset now)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(FieldKeys.Email, f.Email);
        Add(FieldKeys.Phone, f.Phone);
        Add(FieldKeys.Zip, f.Zip);
        Add(FieldKeys.Website, f.Website);
        Add(FieldKeys.Linkedin, f.Linkedin);
        return new Profile(new[] { ProfileNormalizer.Normalize(new Entity("person", fields)) }, now);

        void Add(string key, string? value)
        {
            if (value is not null) fields[key] = value;
        }
    }

    public static Profile Merge(Profile llm, FastFields f) => Merge(llm, FromFastFields(f, llm.CreatedAt));

    // The model's result first. The local result fills only keys the model left out, and only with values whose shape
    // alone says what they are: an email address, or a phone number written like one. The local guesses (a name from a
    // first line, a street, a ZIP code) never add to a model's reading. Local values go to the entity with the same
    // label, then to the model's first entity for the person's own details, else to a new entity. A reply with no
    // usable value is no reading at all, so the local result stands.
    public static Profile Merge(Profile llm, Profile local)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(local);
        if (llm.Keys.Count == 0) return new Profile(local.Entities, llm.CreatedAt);
        var entities = llm.Entities
            .Select(e => (e.Label, Fields: new Dictionary<string, string>(e.Fields, StringComparer.Ordinal)))
            .ToList();
        var llmKeys = new HashSet<string>(llm.Keys, StringComparer.Ordinal);
        foreach (var entity in local.Entities)
        {
            var missing = entity.Fields.Where(f => !llmKeys.Contains(f.Key) && IsSure(f.Key, f.Value)).ToArray();
            if (missing.Length == 0) continue;
            var target = entities.FindIndex(e => e.Label.Equals(entity.Label, StringComparison.OrdinalIgnoreCase));
            if (target < 0 && entity.Label.Equals("person", StringComparison.OrdinalIgnoreCase) && entities.Count > 0) target = 0;
            if (target < 0)
            {
                entities.Add((entity.Label, new Dictionary<string, string>(StringComparer.Ordinal)));
                target = entities.Count - 1;
            }
            foreach (var (key, value) in missing) entities[target].Fields.TryAdd(key, value);
        }
        return Build(entities.Select(e => new Entity(e.Label, e.Fields)), llm.CreatedAt);
    }

    private static bool IsSure(string key, string value)
    {
        try
        {
            return key switch
            {
                FieldKeys.Email => TextHeuristics.IsEmail(value),
                FieldKeys.Phone => TextHeuristics.IsWrittenPhone(value),
                _ => false
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    // The entities as the model wrote them, keys normalized, before names are derived.
    internal static IReadOnlyList<Entity> ParseEntities(string llmContent)
    {
        var json = ExtractObject(llmContent);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("entities", out var entitiesJson) || entitiesJson.ValueKind != JsonValueKind.Array)
                throw new FormatException("Profile JSON must contain an entities array.");

            var entities = new List<Entity>();
            foreach (var item in entitiesJson.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("label", out var labelJson) || labelJson.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("fields", out var fieldsJson) ||
                    fieldsJson.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    throw new FormatException("Each entity must contain a string label and fields.");

                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (fieldsJson.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in fieldsJson.EnumerateObject())
                        Add(property.Name, property.Value);
                }
                else
                {
                    foreach (var pair in fieldsJson.EnumerateArray())
                        if (pair.ValueKind == JsonValueKind.Object &&
                            pair.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String &&
                            pair.TryGetProperty("value", out var value))
                            Add(key.GetString()!, value);
                }
                entities.Add(new Entity(labelJson.GetString()!, fields));

                void Add(string name, JsonElement value)
                {
                    var key = ProfileNormalizer.NormalizeKey(name);
                    var text = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.Number => value.GetRawText(),
                        _ => null
                    };
                    if (key.Length > 0 && !string.IsNullOrWhiteSpace(text)) fields.TryAdd(key, text.Trim());
                }
            }
            return entities;
        }
        catch (JsonException)
        {
            // The JSON parser's message can quote the reply, which can hold copied text, so it is not kept.
            throw new FormatException("Model reply is not valid profile JSON.");
        }
    }

    private static Profile Build(IEnumerable<Entity> entities, DateTimeOffset now) =>
        new(entities.Select(WithoutSecrets).Select(ProfileNormalizer.Normalize).Where(e => e.Fields.Count > 0).ToArray(), now);

    // Whatever a model returns, a profile keeps the standard keys and only those extra keys that do not name a secret
    // ("account_number", "routing_number", "pin"), and no value that holds a card number, an SSN, or an IBAN. A phone
    // number written with its country code ("+86 138 0013 8000") skips the card check: a long one can pass it.
    private static Entity WithoutSecrets(Entity entity)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entity.Fields)
        {
            if (!FieldKeys.Standard.Contains(key) && Redaction.IsSensitiveLabel(LabelNormalizer.Humanize(key))) continue;
            var international = key == FieldKeys.Phone && value.TrimStart().StartsWith('+');
            if (Redaction.HasSensitiveNumber(value, cards: !international)) continue;
            fields[key] = value;
        }
        return fields.Count == entity.Fields.Count ? entity : new Entity(entity.Label, fields);
    }

    private static string ExtractObject(string content)
    {
        if (content is null) throw new FormatException("No JSON object in model reply.");
        var start = content.IndexOf('{');
        if (start < 0) throw new FormatException("No JSON object in model reply.");
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return content[start..(i + 1)];
        }
        throw new FormatException("Unclosed JSON object in model reply.");
    }
}

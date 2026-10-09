using System.Text.Json;

namespace SmartPaste.Core;

public static class ProfileParser
{
    public static Profile Parse(string llmContent, DateTimeOffset now)
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
                    !item.TryGetProperty("fields", out var fieldsJson) || fieldsJson.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Each entity must contain a string label and fields object.");

                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in fieldsJson.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                        fields[property.Name] = property.Value.GetString()!;
                    else if (property.Value.ValueKind == JsonValueKind.Number)
                        fields[property.Name] = property.Value.GetRawText();
                }

                var entity = ProfileNormalizer.Normalize(new Entity(labelJson.GetString()!, fields));
                if (entity.Fields.Count > 0)
                    entities.Add(entity);
            }

            return new Profile(entities, now);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Model reply is not valid profile JSON.", ex);
        }
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

    public static Profile Merge(Profile llm, FastFields f)
    {
        var entities = llm.Entities.ToList();
        if (entities.Count == 0)
            entities.Add(new Entity("person", new Dictionary<string, string>()));

        var firstFields = new Dictionary<string, string>(entities[0].Fields, StringComparer.Ordinal);
        Fill(FieldKeys.Email, f.Email);
        Fill(FieldKeys.Phone, f.Phone);
        Fill(FieldKeys.Zip, f.Zip);
        entities[0] = ProfileNormalizer.Normalize(new Entity(entities[0].Label, firstFields));
        return new Profile(entities, llm.CreatedAt);

        void Fill(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && llm.ValuesFor(key).Count == 0)
                firstFields[key] = value;
        }
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

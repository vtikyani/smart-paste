using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public static class ProfileNormalizer
{
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

        if (!fields.ContainsKey(FieldKeys.FullName) &&
            fields.TryGetValue(FieldKeys.FirstName, out var first) &&
            fields.TryGetValue(FieldKeys.LastName, out var last))
        {
            fields[FieldKeys.FullName] = fields.TryGetValue(FieldKeys.MiddleName, out var middle)
                ? $"{first} {middle} {last}" : $"{first} {last}";
        }

        if (!fields.ContainsKey(FieldKeys.FirstName) && !fields.ContainsKey(FieldKeys.LastName) &&
            fields.TryGetValue(FieldKeys.FullName, out var fullName))
        {
            var parts = fullName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                fields[FieldKeys.FirstName] = parts[0];
                fields[FieldKeys.LastName] = parts[^1];
                if (parts.Length > 2)
                    fields[FieldKeys.MiddleName] = string.Join(' ', parts[1..^1]);
            }
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
    public static string NormalizeKey(string key) => Regex.Replace(key.Trim().ToLowerInvariant(), "[\\s-]+", "_");
}

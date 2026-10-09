using System.Text.Json;

namespace SmartPaste.Core;

public sealed class LabelCache
{
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    // Only learned labels are persisted, so a newer seed is never shadowed by an old copy of itself.
    private readonly Dictionary<string, string> _learned = new(StringComparer.Ordinal);
    private readonly string? _userFilePath;

    public LabelCache(string? userFilePath) : this(userFilePath, true) { }

    private LabelCache(string? userFilePath, bool loadSeed)
    {
        _userFilePath = userFilePath;
        if (loadSeed)
        {
            using var stream = typeof(LabelCache).Assembly.GetManifestResourceStream("SmartPaste.Core.seed-labels.json")
                ?? throw new InvalidOperationException("Embedded seed labels missing.");
            Overlay(JsonSerializer.Deserialize<Dictionary<string, string>>(stream));
        }
        if (userFilePath is not null)
        {
            try { Overlay(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(userFilePath)), _learned); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
    }

    public static LabelCache Empty() => new(null, false);
    public int Count => _labels.Count;

    public bool TryGet(string normalizedLabel, out string key) => _labels.TryGetValue(LabelNormalizer.Normalize(normalizedLabel), out key!);

    // Never throws: a cache write failing must not cost the user the paste it was learned from.
    public void Set(string normalizedLabel, string key)
    {
        var label = LabelNormalizer.Normalize(normalizedLabel);
        if (label.Length == 0 || !FieldKeys.Standard.Contains(key)) return;
        _labels[label] = key;
        _learned[label] = key;
        if (_userFilePath is null) return;

        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_userFilePath))!;
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".{Path.GetFileName(_userFilePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(_learned));
            File.Move(temp, _userFilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { try { if (temp is not null && File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }

    private void Overlay(Dictionary<string, string>? entries, Dictionary<string, string>? learned = null)
    {
        if (entries is null) return;
        foreach (var (label, key) in entries)
            if (FieldKeys.Standard.Contains(key))
            {
                var normalized = LabelNormalizer.Normalize(label);
                _labels[normalized] = key;
                if (learned is not null) learned[normalized] = key;
            }
    }
}

public static class KeywordRules
{
    // Fields that must never receive a profile value and never trigger a network call with profile data.
    public static bool IsSensitive(string normalizedLabel)
    {
        var s = LabelNormalizer.Normalize(normalizedLabel);
        return Any(s, "password", "passcode", "ssn", "social security", "card number", "credit card", "debit card",
            "cvc", "cvv", "security code", "expiration", "expiry", "routing number", "account number", "tax id", "otp",
            "verification code", "one time code");
    }

    public static string? Match(string normalizedLabel)
    {
        var s = LabelNormalizer.Normalize(normalizedLabel);
        if (s.Length == 0) return null;
        if (Any(s, "emergency contact", "reference", "manager", "recruiter", "supervisor", "how did you hear", "cover letter", "promo code", "card number", "authorized to work", "authorization to work", "password", "social security", "ssn", "extension", "ext", "device type", "phone code", "country code", "dial code") || s.Contains("referr", StringComparison.Ordinal)) return null;
        if (Any(s, "company name", "current company", "employer", "organization", "business name")) return FieldKeys.Company;
        if (Any(s, "full name", "full legal name")) return FieldKeys.FullName;
        if (Any(s, "first name", "given name", "forename") || (Has(s, "name") && Any(s, "first", "given"))) return FieldKeys.FirstName;
        if (Any(s, "last name", "family name", "surname")) return FieldKeys.LastName;
        if (Has(s, "middle")) return FieldKeys.MiddleName;
        if (Any(s, "zip", "postal", "postcode", "post code")) return FieldKeys.Zip;
        if (Any(s, "city", "town")) return FieldKeys.City;
        if (Any(s, "state", "province")) return FieldKeys.State;
        if (Has(s, "country")) return FieldKeys.Country;
        if (Any(s, "email", "e mail")) return FieldKeys.Email;
        if (Any(s, "phone", "mobile", "cell", "telephone", "tel")) return FieldKeys.Phone;
        if (Any(s, "apt", "apartment", "suite", "unit", "line 2", "address 2")) return FieldKeys.Unit;
        if (Any(s, "street", "address line 1", "address 1", "address")) return FieldKeys.Street;
        if (Has(s, "linkedin")) return FieldKeys.Linkedin;
        if (Any(s, "website", "portfolio", "url")) return FieldKeys.Website;
        if (Any(s, "birth", "dob")) return FieldKeys.DateOfBirth;
        if (Any(s, "job description")) return null;
        if (Any(s, "title", "position", "role")) return FieldKeys.Title;
        if (s is "name" or "full name" or "your name" or "legal name" or "full legal name" or "applicant name" or "cardholder name") return FieldKeys.FullName;
        return null;
    }

    private static bool Any(string value, params string[] terms) => terms.Any(term => Has(value, term));
    private static bool Has(string value, string term) => (" " + value + " ").Contains(" " + term + " ", StringComparison.Ordinal);
}

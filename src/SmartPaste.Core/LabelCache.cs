using System.Text.Json;

namespace SmartPaste.Core;

public sealed record LearnedLabel(string Label, string Key);

// Field label -> profile key. Two layers: the built-in seed list (shipped with the app) and labels learned on
// this PC (from confident Jev decisions and from the user's picks). Learned labels win over the seed.
// Thread-safe: the paste path, the focus prefetcher, and the settings page all use one instance.
public sealed class LabelCache
{
    // The built-in list, read once from the embedded resource and shared (read-only) by every instance.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> BuiltIn = new(LoadBuiltIn);

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _seed = new(StringComparer.Ordinal);
    // Only learned labels are persisted, so a newer seed is never shadowed by an old copy of itself.
    private readonly Dictionary<string, string> _learned = new(StringComparer.Ordinal);
    private readonly string? _userFilePath;

    public LabelCache(string? userFilePath) : this(userFilePath, true) { }

    private LabelCache(string? userFilePath, bool loadSeed)
    {
        _userFilePath = userFilePath;
        if (loadSeed)
            foreach (var (label, key) in BuiltIn.Value) _seed[label] = key;
        if (userFilePath is not null)
        {
            try { Overlay(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(userFilePath)), _learned); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
    }

    // Raised (on the thread that made the change) after a label is learned or forgotten.
    public event EventHandler? Changed;

    public static LabelCache Empty() => new(null, false);

    public int Count
    {
        get { lock (_gate) return _seed.Keys.Union(_learned.Keys).Count(); }
    }

    // Labels learned on this PC, sorted by label.
    public IReadOnlyList<LearnedLabel> Learned
    {
        get
        {
            lock (_gate)
                return _learned.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new LearnedLabel(p.Key, p.Value)).ToArray();
        }
    }

    public bool TryGet(string normalizedLabel, out string key) => TryGet(normalizedLabel, out key, out _);

    // Like TryGet, and says where the answer came from: a label learned on this PC, or the built-in list.
    public bool TryGet(string normalizedLabel, out string key, out bool learned)
    {
        var label = LabelNormalizer.Normalize(normalizedLabel);
        lock (_gate)
        {
            learned = _learned.TryGetValue(label, out key!);
            return learned || _seed.TryGetValue(label, out key!);
        }
    }

    // The built-in list alone, without anything learned. Thread-safe and allocation-free after first use.
    internal static bool TryGetBuiltIn(string label, out string key) =>
        BuiltIn.Value.TryGetValue(LabelNormalizer.Normalize(label), out key!);

    // Never throws: a cache write failing must not cost the user the paste it was learned from.
    // Sensitive labels are never learned: such fields must never receive a value.
    public void Set(string normalizedLabel, string key)
    {
        var label = LabelNormalizer.Normalize(normalizedLabel);
        if (label.Length == 0 || !FieldKeys.Standard.Contains(key) || KeywordRules.IsSensitive(label)) return;
        lock (_gate)
        {
            if (_learned.TryGetValue(label, out var existing) && existing == key) return;
            _learned[label] = key;
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Removes a learned label. A label that is also in the seed list goes back to its built-in meaning.
    public bool Forget(string label)
    {
        bool removed;
        lock (_gate)
        {
            removed = _learned.Remove(LabelNormalizer.Normalize(label));
            if (removed) Persist();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void ForgetAll()
    {
        lock (_gate)
        {
            if (_learned.Count == 0) return;
            _learned.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Caller holds _gate. Writes to a temp file and renames, so a crash mid-write never corrupts the file.
    private void Persist()
    {
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

    private static IReadOnlyDictionary<string, string> LoadBuiltIn()
    {
        using var stream = typeof(LabelCache).Assembly.GetManifestResourceStream("SmartPaste.Core.seed-labels.json")
            ?? throw new InvalidOperationException("Embedded seed labels missing.");
        var seed = new Dictionary<string, string>(StringComparer.Ordinal);
        Overlay(JsonSerializer.Deserialize<Dictionary<string, string>>(stream), seed);
        return seed;
    }

    private static void Overlay(Dictionary<string, string>? entries, Dictionary<string, string> target)
    {
        if (entries is null) return;
        foreach (var (label, key) in entries)
            if (FieldKeys.Standard.Contains(key))
            {
                var normalized = LabelNormalizer.Normalize(label);
                if (normalized.Length > 0) target[normalized] = key;
            }
    }
}

public static class KeywordRules
{
    // Fields that must never receive a profile value and never trigger a network call with profile data.
    public static bool IsSensitive(string normalizedLabel)
    {
        var s = LabelNormalizer.Normalize(normalizedLabel);
        return Any(s, "password", "passcode", "ssn", "social security", "social insurance", "sin", "card number",
            "credit card", "debit card", "cvc", "cvv", "cvv2", "csc", "security code", "expiration", "expiry",
            "routing number", "account number", "iban", "tax id", "itin", "otp", "verification code",
            "one time code", "auth code", "authentication code", "2fa", "mfa", "security question",
            "security answer", "secret", "passport", "driver license", "drivers license", "driving licence");
    }

    // Text (a label or a section heading) that says the field is about another person, whose details are not in
    // the user's block.
    public static bool IsAboutSomeoneElse(string text)
    {
        var s = LabelNormalizer.Normalize(text);
        return Any(s, "emergency", "emergency contact", "reference", "references", "manager", "recruiter", "supervisor",
            "next of kin", "spouse", "beneficiary", "guardian", "co applicant") || s.Contains("referr", StringComparison.Ordinal);
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

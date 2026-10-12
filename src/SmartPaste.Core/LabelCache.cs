using System.Text.Json;
using System.Text.RegularExpressions;

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

// Rules over field labels: which fields are secret, which are about another person, and (for the offline fallback)
// which profile key a label asks for. Terms are matched as whole words, so "pin" never matches "shipping".
public static partial class KeywordRules
{
    // Secret wherever they appear in a label: passwords and PINs, card and bank details, government and tax ids,
    // one-time codes, security questions. The last word of a term may also take an "s" ("security questions").
    // "PIN code" is also India's postal code; the PIN reading wins, which only costs that field a smart paste.
    private static readonly string[] SecretTerms =
    [
        // Passwords, PINs, keys.
        "password", "passcode", "passphrase", "passwd", "pwd", "pin", "secret", "security key", "private key", "api key",
        "access token", "token",
        // Card details. The word "card" alone is handled separately, so the name and billing address on a card stay usable.
        "card number", "card no", "card num", "card nr", "card verification", "card security", "card code",
        "cc number", "cc num", "cc no", "cc exp", "cc expiry", "cc expiration", "cc cvv", "cc cvc", "cc csc",
        "cvv", "cvv2", "cvc", "cvc2", "cvn", "cvd", "csc", "cid", "cav2", "security code", "pan",
        "expiration", "expiry", "expires", "exp date", "exp month", "exp year", "mm yy", "mmyy",
        "valid thru", "valid through", "valid until", "good thru",
        // Bank details.
        "account number", "account no", "account num", "acct number", "acct no", "acct num", "bank account",
        "checking account", "savings account", "routing", "aba", "sort code", "swift", "bic", "iban", "bsb",
        "transit number", "institution number",
        // Government and tax ids.
        "ssn", "social security", "social insurance", "sin", "national insurance", "ni number", "nino", "national id",
        "national identity", "national identification", "id number", "identification number", "identity number",
        "government id", "tax id", "tax number", "tax identification", "taxpayer", "tin", "ein", "itin", "tfn",
        "aadhaar", "nhs number", "passport", "visa number", "drivers license", "driver license", "drivers licence",
        "driver licence", "driving license", "driving licence", "dl number", "license number", "licence number",
        // One-time and verification codes.
        "otp", "one time", "verification code", "verification number", "verify code", "confirmation code",
        "auth code", "authentication code", "authorization code", "access code", "sms code", "recovery code",
        "backup code", "digit code", "digit pin", "code we sent", "code we texted", "code we emailed", "code sent to",
        "sent you a code", "texted you", "2fa", "mfa", "two factor", "2 factor", "two step", "2 step", "authenticator",
        // Security questions.
        "security question", "secret question", "challenge question", "security answer", "secret answer",
        "memorable word", "memorable information", "maiden name", "first pet", "pets name", "childhood", "grew up",
        "grow up", "first car", "first school", "elementary school", "were you born"
    ];

    // Secret only as the whole label: "Account #" is an account number, "Account email" is not.
    private static readonly HashSet<string> SecretLabels = new(StringComparer.Ordinal)
    {
        "account", "acct", "exp", "dl", "ss", "verification"
    };

    // Ids written as one word ("cardnumber", "ccexp"): whole words.
    private static readonly HashSet<string> SecretIdWords = new(StringComparer.Ordinal)
    {
        "cardnumber", "cardno", "cardnum", "creditcard", "debitcard", "ccnumber", "ccnum", "ccno", "ccexp", "cvvcode",
        "securitycode", "expdate", "expirydate", "expirationdate", "expmonth", "expyear", "accountnumber", "accountno",
        "acctnumber", "acctno", "routingnumber", "sortcode", "swiftcode", "bankaccount", "taxid", "taxpayerid",
        "nationalid", "passportnumber", "passportno", "driverslicense", "driverlicense", "drivinglicence",
        "drivinglicense", "dlnumber", "licensenumber", "socialsecurity", "ssnumber", "onetimecode", "otpcode",
        "verificationcode", "authcode", "securityanswer", "securityquestion", "secretquestion", "secretanswer", "pinnumber"
    };

    // Long enough to be unmistakable inside a longer one-word id ("billingcardnumber", "newpassword2").
    private static readonly string[] SecretIdParts =
    [
        "password", "passcode", "passphrase", "cardnumber", "cardnum", "securitycode", "accountnumber", "acctnumber",
        "routingnumber", "socialsecurity", "verificationcode", "onetimecode", "passport", "cvv", "cvc"
    ];

    // With "card", these make the label about the card's holder or billing address, which are the user's details.
    private static readonly string[] CardHolderWords =
        ["name", "holder", "zip", "postal", "postcode", "address", "street", "city", "state", "country"];

    // Headings and help that make a vague label ("Number", "Code", "Date", or none) a card, bank, or verification field.
    private static readonly string[] SecretTopics =
    [
        "card", "credit", "debit", "payment", "pay", "bank", "banking", "deposit", "wire", "routing", "iban", "swift",
        "verification", "verify", "authentication", "authenticate", "authenticator", "security", "secure", "password",
        "passcode", "pin", "otp", "2fa", "mfa", "two factor", "one time", "identity", "identification", "id", "tax",
        "ssn", "social security", "passport", "license", "licence", "expiry", "expiration", "cvv", "cvc"
    ];

    // Sections in which every field is a secret answer.
    private static readonly string[] SecretSections =
    [
        "security question", "secret question", "challenge question", "security answer", "secret answer",
        "recovery question", "memorable"
    ];

    // Words a label can be made of and still say nothing about what the field holds.
    private static readonly HashSet<string> VagueLabelWords = new(StringComparer.Ordinal)
    {
        "number", "numbers", "no", "num", "nr", "code", "date", "value", "text", "field", "input", "entry", "answer",
        "response", "month", "year", "day", "mm", "yy", "yyyy", "dd", "id", "key", "digit", "digits", "first", "last",
        "enter", "the", "your", "a", "an", "here", "please", "type", "of", "on", "this"
    };

    // A label, heading, or id that marks a field which must never receive a profile value and never trigger a
    // network call with profile data. Labels and raw ids ("ccNumber") both work.
    public static bool IsSensitive(string normalizedLabel)
    {
        if (string.IsNullOrWhiteSpace(normalizedLabel)) return false;
        var words = LabelNormalizer.Normalize(normalizedLabel);
        // "ccNumber" only reads as "cc number" once its camel case is split.
        var split = LabelNormalizer.Humanize(normalizedLabel);
        return IsSecret(words) || (split != words && IsSecret(split));
    }

    // The whole field. Text that names it (label, accessible name, placeholder, id, nearby text, OCR) is checked for
    // secrets first. Help text and section text describe the field and count only when its label does not already say
    // what it is, so "Phone" with help "We'll text a code to this number" stays a phone field. A vague label ("Number",
    // "Code", "Date", or none) under a card, bank, or verification heading, or with such help, is that secret.
    public static bool IsSensitive(FieldContext field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var naming = new[] { field.LabeledBy, field.Name, field.Placeholder, field.AutomationId, field.OcrText }.Concat(field.Siblings);
        if (naming.Any(text => !string.IsNullOrWhiteSpace(text) && (IsSensitive(text!) || SecretMask().IsMatch(text!))))
            return true;
        // Under "Security questions", every answer is a secret, whatever its label.
        if (field.Sections.Any(section => Mentions(LabelNormalizer.Normalize(section), SecretSections))) return true;

        var label = LabelNormalizer.Normalize(field.BestLabel());
        if (label.Length > 0 && (LabelCache.TryGetBuiltIn(label, out _) || Match(label) is not null)) return false;
        var described = field.Sections.Prepend(field.HelpText).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        if (described.Any(text => IsSensitive(text!))) return true;
        return IsVague(label) && described.Any(text => Mentions(LabelNormalizer.Normalize(text), SecretTopics));
    }

    // Text (a label or a section heading) that says the field is about another person, whose details are not in the
    // user's block.
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

    private static bool IsSecret(string s)
    {
        if (s.Length == 0) return false;
        if (SecretLabels.Contains(s) || Mentions(s, SecretTerms)) return true;
        var words = s.Split(' ');
        if (words.Any(word => SecretIdWords.Contains(word) || SecretIdParts.Any(part => word.Contains(part, StringComparison.Ordinal))))
            return true;
        // A card's number, code, or expiry is secret; the name and billing address on it are not.
        return HasTerm(s, "card") && !CardHolderWords.Any(word => HasTerm(s, word));
    }

    // Empty, or made only of words that say nothing about the value ("Number", "Enter the 6 digit code", "1234").
    private static bool IsVague(string label) =>
        label.Length == 0 || label.Split(' ').All(word => VagueLabelWords.Contains(word) || word.All(char.IsDigit));

    private static bool Mentions(string s, string[] terms) => s.Length > 0 && terms.Any(term => HasTerm(s, term));

    // A term as whole words; a term longer than three letters may also end in "s" ("questions", "cards", "parents").
    private static bool HasTerm(string value, string term)
    {
        var padded = " " + value + " ";
        return padded.Contains(" " + term + " ", StringComparison.Ordinal) ||
               (term.Length > 3 && padded.Contains(" " + term + "s ", StringComparison.Ordinal));
    }

    private static bool Any(string value, params string[] terms) => terms.Any(term => Has(value, term));
    private static bool Has(string value, string term) => (" " + value + " ").Contains(" " + term + " ", StringComparison.Ordinal);

    // A card number or SSN pattern shown as a placeholder: "1234 1234 1234 1234", "XXXX-XXXX-XXXX-XXXX", "3782 822463 10005",
    // "XXX-XX-XXXX". Phone ("(555) 555-5555") and ZIP+4 ("12345-6789") patterns do not match.
    [GeneratedRegex(@"^\s*(?:(?:[0-9Xx•*#]{4}[\s-]?){3}[0-9Xx•*#]{3,4}|[0-9Xx•*#]{4}[\s-]?[0-9Xx•*#]{6}[\s-]?[0-9Xx•*#]{5}|[0-9Xx•*#]{3}-[0-9Xx•*#]{2}-[0-9Xx•*#]{4})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretMask();
}

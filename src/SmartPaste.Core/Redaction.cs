using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

// Keeps secrets out of what leaves the PC and out of every profile. A line or " | " part whose label names a secret
// ("Password:", "Card #", "Routing number", "SSN") is removed, and card numbers, US social security numbers, and IBANs
// are masked wherever they appear, labeled or not.
internal static partial class Redaction
{
    // What a masked number becomes in text sent to the parsing model.
    public const string Mask = "[removed]";

    private const int TimeoutMs = 200;

    // Labels for secrets beyond KeywordRules' list: bank details, PINs and codes, government IDs, keys.
    private static readonly string[] SecretTerms =
    [
        "routing", "aba", "iban", "swift", "bic", "bsb", "sort code", "transit number", "institution number",
        "pin", "cvv", "cvv2", "cvc", "cvc2", "csc", "security code", "expiry", "expiration", "exp", "valid thru",
        "valid through", "good thru",
        "ssn", "sin", "social security", "social insurance", "national insurance", "ni number", "national id", "tin",
        "ein", "itin", "tax id", "medicare", "insurance number", "policy number", "passport", "driver license",
        "drivers license", "driver s license", "driving licence", "license number", "licence number",
        "password", "passcode", "passphrase", "pwd", "otp", "one time", "verification code", "auth code",
        "access code", "gate code", "door code", "alarm code", "entry code", "security question", "security answer",
        "secret", "maiden name", "api key", "private key", "secret key", "product key", "license key", "token",
        "recovery code", "backup code"
    ];

    // Card and account labels name a secret ("Card #", "Acct no.") unless they name the holder or a contact detail
    // ("Name on card", "Account email").
    private static readonly string[] CardOrAccount = ["acct", "account", "card"];
    private static readonly string[] HolderWords = ["name", "holder", "owner", "email", "e mail", "phone", "mobile", "cell", "type", "brand", "manager"];

    // Words a secret's label is made of. A line counts as a bare label ("Account number") only when every word is one of
    // these, so a name such as "Taylor Swift" is never taken for the label "SWIFT".
    private static readonly HashSet<string> LabelWords = BuildLabelWords();

    // The text with secrets removed: lines and " | " parts whose label names a secret, a lone label line ("Account
    // number") together with the value on the line below it, and card numbers, SSNs, and IBANs anywhere. Everything else,
    // line breaks included, is left exactly as it was.
    public static string Redact(string text, string mask = Mask)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        var valueBelow = false;
        foreach (var (line, lineBreak) in SplitKeepingBreaks(text))
        {
            var kept = line;
            if (!string.IsNullOrWhiteSpace(line))
            {
                // The value under a lone label, unless that line has a label of its own.
                var isValue = valueBelow && !LabelLines.HasLabel(line);
                valueBelow = false;
                if (isValue) kept = "";
                else if (IsBareLabel(line))
                {
                    kept = "";
                    valueBelow = true;
                }
                else kept = WithoutSecretParts(line);
            }
            builder.Append(MaskNumbers(kept, mask)).Append(lineBreak);
        }
        return builder.ToString();
    }

    // Card numbers, SSNs, and IBANs replaced with the mask; everything else unchanged. Masking one number can leave a
    // neighbor it was glued to standing alone ("4111 1111 1111 1111DE89 ..."), so the text is checked again until
    // nothing is found, a few rounds at most.
    public static string MaskNumbers(string text, string mask = Mask)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var round = 0; round < 3; round++)
        {
            var spans = SecretNumbers(text, cards: true);
            if (spans.Count == 0) break;
            var builder = new StringBuilder(text.Length);
            var position = 0;
            foreach (var (start, end) in spans)
            {
                builder.Append(text, position, start - position).Append(mask);
                position = end;
            }
            text = builder.Append(text, position, text.Length - position).ToString();
        }
        return text;
    }

    // True when the value holds a card number (when cards is true), an SSN, or an IBAN. A value too convoluted to check
    // counts as holding one, so it is never kept.
    public static bool HasSensitiveNumber(string value, bool cards = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return SecretNumbers(value, cards).Count > 0;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    // True when a label ("Card #", "Acct no.", "Routing", "PIN", "Security code") names a secret. KeywordRules decides
    // first; this adds the bank, card, account, and code labels it does not cover.
    public static bool IsSensitiveLabel(string label)
    {
        var s = LabelNormalizer.Normalize(label);
        if (s.Length == 0) return false;
        if (KeywordRules.IsSensitive(s) || SecretTerms.Any(term => Has(s, term))) return true;
        return CardOrAccount.Any(term => Has(s, term)) && !HolderWords.Any(term => Has(s, term));
    }

    // "Account number", "Security code:", "PIN": a line that is only a secret's label.
    private static bool IsBareLabel(string line)
    {
        var s = LabelNormalizer.Normalize(line);
        return s.Length is > 0 and <= 40 && IsLabelWords(s) && IsSensitiveLabel(s);
    }

    private static bool IsLabelWords(string normalized)
    {
        var words = normalized.Split(' ');
        return words.Length <= 5 && words.All(LabelWords.Contains);
    }

    private static string WithoutSecretParts(string line)
    {
        var segments = LabelLines.Segments(line);
        var kept = segments.Where(segment => !IsSecret(segment)).ToArray();
        if (kept.Length == segments.Count) return line;
        return string.Join(" | ", kept.Select(segment => segment.Text.Trim()));
    }

    private static bool IsSecret(LabelLines.Segment segment) =>
        segment.Label is { } label ? IsSensitiveLabel(label) : IsLabelAndNumber(segment.Text);

    // "Routing 021000021", "Acct # 1234-5678", "PIN 1234": a secret's label and a number, with no colon between.
    private static bool IsLabelAndNumber(string text)
    {
        var digit = text.AsSpan().IndexOfAnyInRange('0', '9');
        if (digit <= 0) return false;
        var head = LabelNormalizer.Normalize(text[..digit]);
        return head.Length > 0 && IsLabelWords(head) && IsSensitiveLabel(head) && NumberOnly().IsMatch(text[digit..]);
    }

    // Where card numbers, SSNs, and IBANs sit in the text, as sorted, non-overlapping [start, end) spans.
    private static List<(int Start, int End)> SecretNumbers(string text, bool cards)
    {
        var spans = new List<(int Start, int End)>();
        if (cards) AddCards(text, spans);
        foreach (Match match in Ssn().Matches(text)) spans.Add((match.Index, match.Index + match.Length));
        AddIbans(text, spans);
        if (spans.Count < 2) return spans;

        spans.Sort();
        var merged = new List<(int Start, int End)> { spans[0] };
        foreach (var (start, end) in spans.Skip(1))
        {
            if (start <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else merged.Add((start, end));
        }
        return merged;
    }

    // Card numbers: 13 to 19 digits that pass the Luhn check, written as one run or in the groups cards are printed in.
    // From each group the longest such number wins, so a 19-digit card is never masked short of its end.
    private static void AddCards(string text, List<(int Start, int End)> spans)
    {
        foreach (Match run in DigitGroups().Matches(text))
        {
            var groups = Groups(run);
            // "+44 20 7946 0958": a number after a plus sign is a phone number's country code, not a card.
            var first = run.Index > 0 && text[run.Index - 1] == '+' ? 1 : 0;
            for (var i = first; i < groups.Count; i++)
            {
                var last = i - 1;
                var total = 0;
                while (last + 1 < groups.Count && total + groups[last + 1].Length <= 19) total += groups[++last].Length;
                for (var j = last; j >= i && total >= 13; total -= groups[j--].Length)
                {
                    if (!IsCardGrouping(groups, i, j)) continue;
                    var end = groups[j].Start + groups[j].Length;
                    var digits = new StringBuilder(total);
                    for (var k = i; k <= j; k++) digits.Append(text, groups[k].Start, groups[k].Length);
                    if (!PassesLuhn(digits.ToString())) continue;
                    spans.Add((groups[i].Start, end));
                    i = j;
                    break;
                }
            }
        }
    }

    private static List<(int Start, int Length)> Groups(Match run)
    {
        var groups = new List<(int Start, int Length)>();
        var start = -1;
        for (var k = 0; k <= run.Length; k++)
        {
            var isDigit = k < run.Length && char.IsAsciiDigit(run.Value[k]);
            if (isDigit && start < 0) start = k;
            else if (!isDigit && start >= 0)
            {
                groups.Add((run.Index + start, k - start));
                start = -1;
            }
        }
        return groups;
    }

    // One run of digits, groups of four with a shorter last group ("4111 1111 1111 1111", "6221 2600 0000 0000 000"), or
    // American Express and Diners Club layouts (4-6-5, 4-6-4). Phone numbers and dates are not grouped like this.
    private static bool IsCardGrouping(List<(int Start, int Length)> groups, int first, int last)
    {
        if (first == last) return true;
        var count = last - first + 1;
        if (count == 3 && groups[first].Length == 4 && groups[first + 1].Length == 6 && groups[last].Length is 4 or 5)
            return true;
        for (var k = first; k < last; k++)
            if (groups[k].Length != 4) return false;
        return groups[last].Length is >= 1 and <= 4;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    // IBANs: a country code, two check digits, and up to 30 letters and digits, often in groups of four. The candidate
    // can run on into the words after it, so its longest leading part that passes the IBAN checksum is masked.
    private static void AddIbans(string text, List<(int Start, int End)> spans)
    {
        foreach (Match match in IbanCandidate().Matches(text))
        {
            var parts = match.Value.Split(' ');
            for (var count = parts.Length; count >= 1; count--)
            {
                var compact = string.Concat(parts.Take(count));
                if (!IsIban(compact)) continue;
                var length = parts.Take(count).Sum(part => part.Length) + count - 1;
                spans.Add((match.Index, match.Index + length));
                break;
            }
        }
    }

    // The ISO 13616 check: the first four characters moved to the end, letters as 10 to 35, the number mod 97 is 1.
    private static bool IsIban(string compact)
    {
        if (compact.Length is < 15 or > 34) return false;
        var remainder = 0;
        foreach (var c in compact[4..] + compact[..4])
        {
            if (char.IsAsciiDigit(c)) remainder = (remainder * 10 + (c - '0')) % 97;
            else if (char.IsAsciiLetter(c)) remainder = (remainder * 100 + (char.ToUpperInvariant(c) - 'A' + 10)) % 97;
            else return false;
        }
        return remainder == 1;
    }

    private static IEnumerable<(string Line, string Break)> SplitKeepingBreaks(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var length = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            yield return (text[start..i], text.Substring(i, length));
            i += length - 1;
            start = i + 1;
        }
        yield return (text[start..], "");
    }

    private static HashSet<string> BuildLabelWords()
    {
        var words = new HashSet<string>(StringComparer.Ordinal)
        {
            "number", "numbers", "no", "num", "nbr", "code", "codes", "id", "details", "detail", "info", "information",
            "date", "bank", "banking", "credit", "debit", "checking", "savings", "your", "my", "the", "of", "on", "for",
            "and", "or", "s", "card", "account", "acct", "security", "social", "question", "answer", "verification",
            "authentication", "auth", "one", "time", "2fa", "mfa", "new", "old", "current", "confirm", "primary",
            "mother", "maiden", "name", "key", "personal", "identification"
        };
        foreach (var term in SecretTerms) words.UnionWith(term.Split(' '));
        return words;
    }

    private static bool Has(string value, string term) => (" " + value + " ").Contains(" " + term + " ", StringComparison.Ordinal);

    // Digit groups joined by single spaces or dashes ("4111 1111 1111 1111", "4111-1111-1111-1111").
    [GeneratedRegex(@"(?<!\d)\d+(?:[  -]\d+)*", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex DigitGroups();

    [GeneratedRegex(@"(?<![\d-])\d{3}-\d{2}-\d{4}(?![\d-])", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex Ssn();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[A-Za-z]{2}\d{2}(?:[ ]?[A-Za-z0-9]){11,30}(?![\p{L}\p{N}])", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex IbanCandidate();

    [GeneratedRegex(@"^\d[\d \t./-]*$", RegexOptions.CultureInvariant, TimeoutMs)]
    private static partial Regex NumberOnly();
}

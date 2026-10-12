using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.Core;

public static partial class LabelNormalizer
{
    // Words that framework-generated ids are built from. An id made only of these, numbers, and hex is not a label.
    private static readonly HashSet<string> GenericIdWords = new(StringComparer.Ordinal)
    {
        "input", "inputs", "inp", "field", "fields", "fld", "text", "txt", "textbox", "textarea", "box", "control",
        "ctl", "ctrl", "mat", "form", "el", "elem", "element", "edit", "editor", "item", "value", "val", "entry",
        "data", "main", "content", "body", "container", "wrapper", "root", "page", "panel", "section", "group",
        "react", "select", "downshift", "headlessui", "combobox", "mui", "radix", "ext", "gen", "comp", "cmp",
        "ember", "vue", "aria", "auto", "uid", "guid", "uuid", "ref", "widget", "component", "question", "answer",
        "response", "custom", "dynamic", "generated", "lbl", "label", "ddl", "dropdown", "option", "options", "list",
        "picker", "inner", "outer", "base", "default", "tmp", "temp", "type", "number", "num"
    };

    public static string Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var value = RequiredTag().Replace(label.ToLowerInvariant(), " ");
        value = RequiredWord().Replace(value, " ");
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            // An apostrophe joins rather than splits: "Driver's license" reads as "drivers license", the same as the
            // common spelling without it, instead of "driver s license", which no rule would recognize.
            if (IsApostrophe(c)) continue;
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    // Splits an automation id into words: "shippingZipCode" -> "shipping zip code", "addressLine2" -> "address line 2".
    public static string Humanize(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId)) return "";
        var value = CamelBoundary().Replace(automationId, "$1 $2");
        value = AcronymBoundary().Replace(value, "$1 $2");
        value = LetterDigitBoundary().Replace(value, "$1 $2");
        value = DigitLetterBoundary().Replace(value, "$1 $2");
        return Normalize(value);
    }

    // The id as words when it reads like a label, otherwise null. Web frameworks generate ids such as "input-15",
    // ":r3:", "mat-input-2", "field_8f3a2c", GUIDs, or "ctl00$main$txt1"; those say nothing about the field, and
    // using them as labels would hide the real label and teach the label cache junk.
    public static string? MeaningfulId(string? automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId)) return null;
        var words = Humanize(automationId);
        if (words.Length == 0) return null;
        // With digits around, an all a-f word is most likely a piece of a hex string or GUID.
        var hasDigit = automationId.Any(char.IsAsciiDigit);
        foreach (var word in words.Split(' '))
        {
            if (word.Length >= 3 && word.All(char.IsLetter) && !GenericIdWords.Contains(word) &&
                !(hasDigit && word.All(c => c is >= 'a' and <= 'f')))
                return words;
        }
        return null;
    }

    // Straight and typographic apostrophes, and the marks keyboards and fonts put in their place.
    private static bool IsApostrophe(char c) => c is '\'' or '’' or '‘' or 'ʼ' or '`' or '´' or '′';

    [GeneratedRegex(@"\((?:required|optional)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredTag();
    [GeneratedRegex(@"\brequired\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredWord();
    [GeneratedRegex(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();
    [GeneratedRegex(@"([A-Z])([A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundary();
    [GeneratedRegex(@"([A-Za-z])([0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex LetterDigitBoundary();
    [GeneratedRegex(@"([0-9])([A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex DigitLetterBoundary();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

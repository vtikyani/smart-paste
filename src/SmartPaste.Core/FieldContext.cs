using System.Text.Json.Nodes;

namespace SmartPaste.Core;

public sealed record FieldContext(
    string? Name, string? HelpText, string? AutomationId, string? LabeledBy, string? ControlType,
    bool IsPassword, IReadOnlyList<string> Siblings, string? OcrText = null, int? MaxLength = null)
{
    // Jev reads best from small, relevant state, so every string and list sent to it is capped.
    internal const int StateTextLimit = 120;
    internal const int StateListLimit = 6;

    // Placeholder text shown inside the empty field, when the app exposes it separately from the help text.
    public string? Placeholder { get; init; }

    // Names of the groups or headings the field sits under, nearest first ("Shipping address", "Contact").
    // Never used as the label itself; it is context for telling billing from shipping and the like.
    public IReadOnlyList<string> Sections { get; init; } = [];

    // Executable name of the app that owns the field, for example "chrome.exe".
    public string? AppName { get; init; }

    // The automation id as words when it reads like a label ("shipping zip code"); null for generated ids.
    public string? MeaningfulAutomationId => LabelNormalizer.MeaningfulId(AutomationId);

    public bool HasUsableLabel => BestLabel().Length > 0;

    // True when there is anything to decide from: a label of any kind, or the sections the field sits in.
    public bool HasContext => HasUsableLabel || Sections.Any(s => !string.IsNullOrWhiteSpace(s));

    // The text that best names the field, most reliable source first. Automation ids count only when they read
    // like words, so a generated id never hides the visible label next to the field.
    public string BestLabel() => PickLabel().Text;

    // The label the label cache may learn from: the best label, but only when it is text a person reads on screen
    // (labeled-by element, accessible name, placeholder, nearby text, or OCR). Null when the best label is help
    // text or an automation id, so the cache never learns from ids.
    public string? LearnableLabel()
    {
        var (text, readable) = PickLabel();
        return readable && text.Length > 0 ? text : null;
    }

    // The field as Jev's state: each source under its own name, trimmed, with empty and repeated parts left out.
    public JsonObject ToJevState()
    {
        var state = new JsonObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddText("label", Usable(LabeledBy) ? LabeledBy : Name);
        AddText("placeholder", Placeholder);
        AddText("help_text", HelpText);
        AddText("field_id", MeaningfulAutomationId);
        AddText("control", ControlName(ControlType));
        if (MaxLength is int maxLength && maxLength > 0) state["max_length"] = maxLength;
        // Siblings run farthest to nearest, so keep the nearest ones.
        AddList("nearby_text", Siblings.Where(Usable).TakeLast(StateListLimit));
        AddList("sections", Sections.Where(Usable).Take(StateListLimit));
        AddText("ocr_text", OcrText);
        AddText("app", AppLabel(AppName));
        return state;

        void AddText(string name, string? value)
        {
            if (Usable(value) && IsNew(Trim(value!)) is { } text) state[name] = text;
        }

        void AddList(string name, IEnumerable<string> values)
        {
            var items = new JsonArray();
            foreach (var value in values)
                if (IsNew(Trim(value)) is { } text) items.Add(text);
            if (items.Count > 0) state[name] = items;
        }

        // The same words under two names only add noise.
        string? IsNew(string text)
        {
            var key = LabelNormalizer.Normalize(text);
            return key.Length > 0 && seen.Add(key) ? text : null;
        }
    }

    private (string Text, bool Readable) PickLabel()
    {
        var sources = new (string? Text, bool Readable)[]
        {
            (LabeledBy, true), (Name, true), (Placeholder, true), (HelpText, false), (MeaningfulAutomationId, false),
            (Siblings.LastOrDefault(Usable), true), (OcrText, true)
        };
        foreach (var (text, readable) in sources)
            if (Usable(text)) return (text!.Trim(), readable);
        return ("", false);
    }

    private static bool Usable(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Trim(string value)
    {
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= StateTextLimit ? text : text[..StateTextLimit].TrimEnd();
    }

    private static string? ControlName(string? controlType) => controlType?.Trim() switch
    {
        null or "" => null,
        var type when type.Equals("Edit", StringComparison.OrdinalIgnoreCase) => "text box",
        var type when type.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) => "combo box",
        var type => type
    };

    private static string? AppLabel(string? appName)
    {
        if (!Usable(appName)) return null;
        var name = appName!.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

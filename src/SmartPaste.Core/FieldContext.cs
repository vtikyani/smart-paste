namespace SmartPaste.Core;

public sealed record FieldContext(
    string? Name, string? HelpText, string? AutomationId, string? LabeledBy, string? ControlType,
    bool IsPassword, IReadOnlyList<string> Siblings, string? OcrText = null, int? MaxLength = null)
{
    // Placeholder text shown inside the empty field, when the app exposes it separately from the help text.
    public string? Placeholder { get; init; }

    // Names of the groups or headings the field sits under, nearest first ("Shipping address", "Contact").
    // Never used as the label itself; it is context for telling billing from shipping and the like.
    public IReadOnlyList<string> Sections { get; init; } = [];

    // Executable name of the app that owns the field, for example "chrome.exe".
    public string? AppName { get; init; }

    public string BestLabel()
    {
        foreach (var item in new[] { LabeledBy, Name, HelpText, LabelNormalizer.Humanize(AutomationId ?? ""),
                     Siblings.LastOrDefault(), OcrText })
            if (!string.IsNullOrWhiteSpace(item)) return item.Trim();
        return "";
    }

    public bool HasUsableLabel => BestLabel().Length > 0;

    public string DescribeForJev()
    {
        var lines = new List<string>();
        Add("Field label", BestLabel());
        Add("Help text", HelpText);
        Add("Id", AutomationId);
        Add("Control", ControlType);
        var nearby = Siblings.Where(s => !string.IsNullOrWhiteSpace(s)).Take(4).Select(s => Cut(s, 80));
        Add("Nearby text", string.Join(" | ", nearby));
        return string.Join("\n", lines);
        void Add(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{name}: {Cut(value, 80)}"); }
    }

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}

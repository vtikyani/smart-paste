using CommunityToolkit.Mvvm.ComponentModel;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;
using SmartPaste.Core;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Tray;

// The armed block as the flyout shows it: what it holds, where it came from, and how long it stays armed.
internal sealed partial class ArmedBlockViewModel : ObservableObject
{
    public ArmedBlockViewModel(ArmedProfile armed, bool reveal, DateTimeOffset now)
    {
        Source = armed;
        CountText = StatusText.FieldCount(armed.FieldCount);
        OriginText = armed.Origin == ProfileOrigin.Screenshot ? "from a screenshot" : "from your copy";
        OriginSymbol = armed.Origin == ProfileOrigin.Screenshot ? SymbolRegular.Screenshot20 : SymbolRegular.ClipboardTextLtr20;
        ReaderText = armed.ParsedByAi ? "Read with AI" : "Read on this PC";

        // One group per entity; group titles only when there is more than one ("Person", "Shipping address").
        var entities = armed.Profile.Entities;
        var titled = entities.Count > 1;
        Groups = entities
            .Select(entity => new FieldGroupViewModel(
                titled ? Title(entity.Label) : null,
                armed.Profile.Keys
                    .Where(entity.Fields.ContainsKey)
                    .Select(key => new FieldRowViewModel(FieldKeys.DisplayName(key), entity.Fields[key], reveal))
                    .ToArray()))
            .Where(group => group.Rows.Count > 0)
            .ToArray();
        Tick(now);
    }

    public ArmedProfile Source { get; }

    public string CountText { get; }

    public string OriginText { get; }

    public SymbolRegular OriginSymbol { get; }

    // Whether the block was read by the parsing model or by the local rules alone.
    public string ReaderText { get; }

    public IReadOnlyList<FieldGroupViewModel> Groups { get; }

    [ObservableProperty]
    public partial string ClearsInText { get; private set; } = "";

    // Share of the armed time still left, from 1 down to 0.
    [ObservableProperty]
    public partial double TimeLeft { get; private set; }

    public void Tick(DateTimeOffset now)
    {
        var remaining = Source.ExpiresAt - now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        ClearsInText = StatusText.ClearsIn(remaining);
        var total = Source.ExpiresAt - Source.ArmedAt;
        TimeLeft = total > TimeSpan.Zero ? Math.Clamp(remaining / total, 0, 1) : 0;
    }

    public void SetReveal(bool reveal)
    {
        foreach (var group in Groups)
            foreach (var row in group.Rows) row.SetReveal(reveal);
    }

    private static string Title(string label) => string.IsNullOrWhiteSpace(label) ? "Details" : DisplayText.Sentence(label);
}

internal sealed record FieldGroupViewModel(string? Title, IReadOnlyList<FieldRowViewModel> Rows)
{
    public bool HasTitle => Title is not null;

    // What a screen reader calls the group. A single group shows no title on screen.
    public string SpokenTitle => Title ?? "Copied values";

    // Screen readers fall back to this for a list item; never the record's field dump.
    public override string ToString() => SpokenTitle;
}

// One field of the armed block: its name and its value, hidden behind dots until the user reveals values.
internal sealed partial class FieldRowViewModel : ObservableObject
{
    private readonly string _value;

    public FieldRowViewModel(string name, string value, bool reveal)
    {
        Name = name;
        _value = DisplayText.OneLine(value, 160);
        SetReveal(reveal);
    }

    public string Name { get; }

    [ObservableProperty]
    public partial string DisplayValue { get; private set; } = "";

    // What a screen reader says for the row and for its value: "hidden", never the dots.
    [ObservableProperty]
    public partial string SpokenText { get; private set; } = "";

    [ObservableProperty]
    public partial string SpokenValue { get; private set; } = "";

    public void SetReveal(bool reveal)
    {
        DisplayValue = reveal ? _value : DisplayText.Masked;
        SpokenValue = reveal ? _value : "hidden";
        SpokenText = $"{Name}: {SpokenValue}";
    }

    public override string ToString() => SpokenText;
}

// One past smart paste: the form's label, the kind of value, who decided, and when. Never the value itself.
internal sealed partial class RecentEntryViewModel : ObservableObject
{
    private readonly DateTimeOffset _at;

    public RecentEntryViewModel(ActivityEntry entry, DateTimeOffset now)
    {
        _at = entry.At;
        Label = string.IsNullOrWhiteSpace(entry.FieldLabel) ? "Unlabeled field" : DisplayText.OneLine(entry.FieldLabel, 48);
        Outcome = entry.Outcome;
        Symbol = entry.Outcome switch
        {
            ActivityOutcome.Pasted => SymbolRegular.Checkmark16,
            ActivityOutcome.PastedOriginal => SymbolRegular.ClipboardPaste16,
            _ => SymbolRegular.Subtract16
        };
        var what = entry.Outcome switch
        {
            ActivityOutcome.Pasted => Join(entry.KeyDisplayName ?? "Value", Decider(entry.DecidedBy)),
            ActivityOutcome.PastedOriginal => "Pasted your clipboard as is",
            _ => "Nothing pasted"
        };
        Detail = Join(what, DisplayText.AppName(entry.AppName));
        Tick(now);
    }

    public string Label { get; }

    public string Detail { get; }

    public SymbolRegular Symbol { get; }

    public ActivityOutcome Outcome { get; }

    public bool IsSmartPaste => Outcome == ActivityOutcome.Pasted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpokenText))]
    public partial string When { get; private set; } = "";

    // What a screen reader says for the entry: "Email address, Email · Jev · chrome, 4 min ago".
    public string SpokenText => $"{Label}, {Detail}, {When}";

    public void Tick(DateTimeOffset now) => When = DisplayText.Ago(_at, now);

    public override string ToString() => SpokenText;

    private static string Decider(DecisionMaker? decider) => decider switch
    {
        DecisionMaker.Jev => "Jev",
        DecisionMaker.User => "your choice",
        DecisionMaker.BuiltIn => "built-in labels",
        DecisionMaker.Learned => "learned label",
        _ => ""
    };

    private static string Join(string first, string second) =>
        string.IsNullOrWhiteSpace(second) ? first : first + " · " + second;
}

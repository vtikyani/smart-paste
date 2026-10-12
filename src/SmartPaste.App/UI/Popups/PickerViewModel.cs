using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;
using SmartPaste.Core;

namespace SmartPaste.App.UI.Popups;

// The picker's content for one request: a title, the candidate rows (numbered 1 to 9), the selection, and in
// "all values" mode the text typed to filter them.
internal sealed partial class PickerViewModel : ObservableObject
{
    public const int Numbered = 9;

    private IReadOnlyList<Candidate> _candidates = [];
    private bool _showEntities;

    public ObservableCollection<PickerRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsFilterable { get; private set; }

    [ObservableProperty]
    public partial string Filter { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasFilter { get; private set; }

    [ObservableProperty]
    public partial bool HasNoMatch { get; private set; }

    [ObservableProperty]
    public partial int SelectedIndex { get; private set; }

    public PickerRow? Selected => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;

    public void Load(PickRequest request)
    {
        IsFilterable = request.Mode == PickMode.AllValues;
        // A short list for a choice Jev could not make; every value when the user asked for all of them.
        _candidates = IsFilterable ? request.Candidates : request.Candidates.Take(Numbered).ToArray();
        // Entity chips only help when they tell rows apart (billing vs shipping), not when every row says "Person".
        _showEntities = _candidates.Select(c => c.EntityLabel.Trim()).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        Title = IsFilterable
            ? "Paste a value"
            : string.IsNullOrWhiteSpace(request.FieldLabel)
                ? "Which one?"
                : $"Which one for “{DisplayText.OneLine(request.FieldLabel, 40)}”?";
        Filter = "";
        Apply();
    }

    public void Type(char character)
    {
        if (!IsFilterable) return;
        Filter += character;
        Apply();
    }

    public void Backspace()
    {
        if (!IsFilterable || Filter.Length == 0) return;
        Filter = Filter[..^1];
        Apply();
    }

    // Moves the selection, stopping at the ends.
    public void Move(int delta)
    {
        if (Rows.Count == 0) return;
        Select(Math.Clamp(SelectedIndex + delta, 0, Rows.Count - 1));
    }

    public void MoveToEnd(bool last) => Select(last ? Rows.Count - 1 : 0);

    // The row a number key picks: 1 is the first visible row. Null when there is no such row.
    public PickerRow? RowForNumber(int number) =>
        number >= 1 && number <= Math.Min(Numbered, Rows.Count) ? Rows[number - 1] : null;

    public void Select(int index)
    {
        if (Rows.Count == 0) return;
        if (Selected is { } previous) previous.IsSelected = false;
        SelectedIndex = index;
        if (Selected is { } current) current.IsSelected = true;
        OnPropertyChanged(nameof(Selected));
    }

    private void Apply()
    {
        HasFilter = Filter.Length > 0;
        var culture = CultureInfo.CurrentCulture.CompareInfo;
        var matches = _candidates.Where(c => !HasFilter
            || culture.IndexOf(c.Value, Filter, CompareOptions.IgnoreCase) >= 0
            || culture.IndexOf(c.KeyDisplayName, Filter, CompareOptions.IgnoreCase) >= 0
            || culture.IndexOf(c.EntityLabel, Filter, CompareOptions.IgnoreCase) >= 0);
        Rows.Clear();
        var index = 0;
        foreach (var candidate in matches) Rows.Add(new PickerRow(candidate, index++, _showEntities));
        HasNoMatch = Rows.Count == 0 && HasFilter;
        SelectedIndex = -1;
        Select(0);
    }
}

// One choice in the picker.
internal sealed partial class PickerRow : ObservableObject
{
    public PickerRow(Candidate candidate, int index, bool showEntity)
    {
        Candidate = candidate;
        Number = index < PickerViewModel.Numbered ? (index + 1).ToString(CultureInfo.InvariantCulture) : "";
        Name = candidate.KeyDisplayName;
        Entity = showEntity && !string.IsNullOrWhiteSpace(candidate.EntityLabel) ? DisplayText.Sentence(candidate.EntityLabel) : null;
        Value = DisplayText.OneLine(candidate.Value, 120);
    }

    public Candidate Candidate { get; }

    // "1" to "9", or empty past the ninth row.
    public string Number { get; }

    public bool HasNumber => Number.Length > 0;

    public string Name { get; }

    public string? Entity { get; }

    public string Value { get; }

    public string SpokenText => Entity is null ? $"{Name}: {Value}" : $"{Name}, {Entity}: {Value}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

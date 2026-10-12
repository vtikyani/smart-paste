using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.UI.Settings.Pages;

// One learned label: the field label as Smart Paste stores it, and the kind of value it means.
internal sealed record LearnedLabelRow(string Label, string Kind)
{
    // What a screen reader says for the row: "Given name: First name".
    public override string ToString() => $"{Label}: {Kind}";
}

// The Learned labels page: what this PC has learned, with Forget and Forget all. Follows LabelCache live.
internal sealed partial class LearnedLabelsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;
    private IReadOnlyList<LearnedLabelRow> _all = [];

    public LearnedLabelsViewModel(SettingsContext context)
    {
        _context = context;
        Reload();
        context.Services.Labels.Changed += OnLabelsChanged;
    }

    // The labels that match the filter, sorted by label.
    public ObservableCollection<LearnedLabelRow> Labels { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabels), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(ForgetAllCommand))]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasNoMatches { get; set; }

    public bool HasLabels => Count > 0;

    public bool IsEmpty => Count == 0;

    partial void OnFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Forget(LearnedLabelRow row)
    {
        if (_context.Services.Labels.Forget(row.Label)) Log.Event("label_forgotten", ("count", 1));
    }

    [RelayCommand(CanExecute = nameof(HasLabels))]
    private async Task ForgetAllAsync()
    {
        var count = Count;
        var confirmed = await _context.Prompts.ConfirmAsync(new Confirmation(
            "Forget all learned labels?",
            $"Smart Paste will forget {StatusText.Count(count, "label", "labels")}. When Jev can't be reached, it will rely on its built-in list until it learns them again.",
            "Forget all"));
        if (!confirmed) return;
        _context.Services.Labels.ForgetAll();
        Log.Event("labels_forgotten", ("count", count));
    }

    public void Dispose() => _context.Services.Labels.Changed -= OnLabelsChanged;

    // LabelCache raises Changed on whichever thread learned the label (often the paste pipeline).
    private void OnLabelsChanged(object? sender, EventArgs e) => _context.Post(Reload);

    private void Reload()
    {
        _all = _context.Services.Labels.Learned
            .Select(l => new LearnedLabelRow(l.Label, FieldKeys.DisplayName(l.Key)))
            .ToArray();
        Count = _all.Count;
        CountText = StatusText.Count(Count, "label", "labels");
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        var visible = filter.Length == 0
            ? _all
            : _all.Where(r => r.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                              r.Kind.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (!visible.SequenceEqual(Labels))
        {
            Labels.Clear();
            foreach (var row in visible) Labels.Add(row);
        }
        HasNoMatches = Count > 0 && Labels.Count == 0;
    }
}

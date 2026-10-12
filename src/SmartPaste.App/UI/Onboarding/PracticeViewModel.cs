using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings;

namespace SmartPaste.App.UI.Onboarding;

internal enum PracticeFieldState { Empty, Deciding, Filled, Problem }

// One field of the practice form.
internal sealed partial class PracticeFieldViewModel : ObservableObject
{
    private bool _filling;

    public PracticeFieldViewModel(string label)
    {
        Label = label;
    }

    // What the field is called on screen, and exactly what the decision pipeline is given.
    public string Label { get; }

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeciding), nameof(IsFilled))]
    public partial PracticeFieldState State { get; set; }

    // Who decided, or what went wrong.
    [ObservableProperty]
    public partial string Caption { get; set; } = "";

    [ObservableProperty]
    public partial bool DecidedByJev { get; set; }

    // Goes up by one for every smart paste into this field; the view plays the highlighter when it changes.
    [ObservableProperty]
    public partial int FillCount { get; set; }

    public bool IsDeciding => State == PracticeFieldState.Deciding;

    public bool IsFilled => State == PracticeFieldState.Filled;

    public void Fill(string value, string caption, bool decidedByJev)
    {
        _filling = true;
        try
        {
            Value = value;
        }
        finally
        {
            _filling = false;
        }
        Caption = caption;
        DecidedByJev = decidedByJev;
        State = PracticeFieldState.Filled;
        FillCount++;
    }

    public void ShowProblem(string message)
    {
        Caption = message;
        DecidedByJev = false;
        State = PracticeFieldState.Problem;
    }

    // Typing over a smart-pasted value makes it the user's own again.
    partial void OnValueChanged(string value)
    {
        if (_filling || State == PracticeFieldState.Deciding) return;
        State = PracticeFieldState.Empty;
        Caption = "";
    }

    // What a screen reader says for the field in the practice form's list.
    public override string ToString() => Label;
}

// The "Try it" step: a sample contact block to copy and a small form to paste into. Ctrl+V in the form runs the
// real decision pipeline in-process (IPracticeEngine), so nothing here is staged.
internal sealed partial class PracticeViewModel : ObservableObject, IDisposable
{
    // Tutorial content for this exercise only, with reserved example values: example.com addresses and 555-01xx
    // phone numbers never belong to a real person.
    public const string SampleBlock = "Jordan Rivera\njordan.rivera@example.com\n(201) 555-0143\n48 Maple Avenue\nHoboken, NJ 07030";

    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(20);

    private readonly SettingsContext _context;
    private readonly CancellationTokenSource _lifetime = new();

    public PracticeViewModel(SettingsContext context)
    {
        _context = context;
        // "State name" asks for the full name, so the sample's "NJ" comes out as "New Jersey".
        Fields = [new("First name"), new("Email address"), new("ZIP code"), new("State name")];
        foreach (var field in Fields) field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PracticeFieldViewModel.State)) RefreshProgress();
        };
        RefreshProgress();
    }

    public IReadOnlyList<PracticeFieldViewModel> Fields { get; }

    public IReadOnlyList<string> SampleLines { get; } = SampleBlock.Split('\n');

    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    [ObservableProperty]
    public partial string CopyProblem { get; set; } = "";

    [ObservableProperty]
    public partial bool IsComplete { get; set; }

    [ObservableProperty]
    public partial string Guidance { get; set; } = "";

    [RelayCommand]
    private void CopySample()
    {
        try
        {
            _context.Services.Clipboard.SetPracticeText(SampleBlock);
            CopyProblem = "";
            IsCopied = true;
            RefreshProgress();
        }
        catch (ExternalException ex)
        {
            Log.Error("practice_copy_error", ex);
            CopyProblem = "Another app is using the clipboard. Try again in a moment.";
        }
    }

    // Ctrl+V in a practice field: read the clipboard now and let the real pipeline decide.
    public async Task PasteAsync(PracticeFieldViewModel field)
    {
        if (field.IsDeciding) return;
        string? text;
        try
        {
            text = _context.Services.Clipboard.GetText();
        }
        catch (ExternalException ex)
        {
            Log.Error("practice_paste_error", ex);
            field.ShowProblem("Another app is using the clipboard. Try again in a moment.");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            field.ShowProblem("Your clipboard is empty. Copy the sample first.");
            return;
        }

        field.State = PracticeFieldState.Deciding;
        field.Caption = "Deciding…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(DecisionTimeout);
        try
        {
            var result = await _context.Services.Practice.PasteIntoAsync(field.Label, text, timeout.Token);
            if (result is null)
            {
                field.ShowProblem("Nothing you copied fits this field. Copy the sample, then try again.");
                return;
            }
            var byJev = IsJev(result.DecidedBy);
            field.Fill(result.Value, Describe(result, byJev, text), byJev);
            Log.Event("practice_paste", ("source", byJev ? "jev" : "local"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            field.ShowProblem("That took too long. Check your internet connection, then try again.");
        }
        catch (Exception ex)
        {
            // The engine handles expected failures itself (it falls back to built-in labels); this is a bug.
            Log.Error("practice_engine_error", ex);
            field.ShowProblem("Something went wrong. Try again.");
        }
    }

    // DecidedBy names who chose: a DecisionMaker ("Jev", "Learned", "BuiltIn") or a FieldDecider source
    // ("jev", "cache", "rules").
    public static bool IsJev(string decidedBy) => decidedBy.Equals(nameof(DecisionMaker.Jev), StringComparison.OrdinalIgnoreCase);

    // Who chose the value, in words.
    private string Describe(PracticeResult result, bool byJev, string clipboardText)
    {
        // The value isn't in the copied text word for word when it was reshaped for the field (NJ to New Jersey).
        var reshaped = !clipboardText.Contains(result.Value, StringComparison.OrdinalIgnoreCase);
        if (byJev) return reshaped ? "Jev picked this and formatted it for the field." : "Jev picked this.";

        var who = result.DecidedBy.ToLowerInvariant() switch
        {
            "learned" or "cache" => "A label this PC learned picked this",
            _ => "Built-in labels picked this"
        };
        var keys = _context.Services.Keys;
        var hasKey = keys.Exists(CredentialStore.OpenRouter) || keys.Exists(CredentialStore.TypeSafe);
        return hasKey ? who + ", because Jev couldn't be reached." : who + ". Add a key so Jev can decide.";
    }

    private void RefreshProgress()
    {
        var filled = Fields.Count(f => f.IsFilled);
        IsComplete = filled == Fields.Count;
        Guidance = IsComplete
            ? "That's Smart Paste. Each field got just the part it asked for."
            : filled > 0
                ? $"{filled} of {Fields.Count} filled. Click the next field and press Ctrl+V."
                : IsCopied
                    ? "Now click a field on the right and press Ctrl+V."
                    : "Copy the sample, click a field, and press Ctrl+V.";
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

using System.Diagnostics;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// What Ctrl+V does while a block is armed, and what the picker shortcut does. Every Ctrl+V the keyboard hook hands
// over ends in exactly one of: the value the field wants, a normal paste, or nothing (the user dismissed the picker,
// or moved to another field or window before the paste could land). Keys typed meanwhile are held by the hook and
// replayed in order once the request is completed, which happens only after the user's clipboard is back.
//
// UI thread: every member, except ShouldIntercept, which the hook calls on its own thread.
internal sealed class SmartPasteService : IDisposable
{
    private static readonly TimeSpan InspectBudget = TimeSpan.FromMilliseconds(600);
    // The recheck is one round trip, but it may wait in line behind an ask-ahead inspection.
    private static readonly TimeSpan RecheckBudget = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan WorkingDelay = TimeSpan.FromMilliseconds(250);

    private readonly KeyboardHook _hook;
    private readonly HotkeyManager _hotkeys;
    private readonly InterceptGate _gate;
    private readonly FocusInspector _focus;
    private readonly ClipboardPaster _paster;
    private readonly ProfileStore _profiles;
    private readonly DecisionService _decisions;
    private readonly FocusPrefetcher _prefetch;
    private readonly IPasteFeedback _feedback;
    private readonly SettingsStore _settings;
    private readonly AppState _state;
    private readonly Dispatcher _dispatcher;
    private bool _active;
    private bool _picking;

    public SmartPasteService(KeyboardHook hook, HotkeyManager hotkeys, InterceptGate gate, FocusInspector focus,
        ClipboardPaster paster, ProfileStore profiles, DecisionService decisions, FocusPrefetcher prefetch,
        IPasteFeedback feedback, SettingsStore settings, AppState state, Dispatcher dispatcher)
    {
        _hook = hook;
        _hotkeys = hotkeys;
        _gate = gate;
        _focus = focus;
        _paster = paster;
        _profiles = profiles;
        _decisions = decisions;
        _prefetch = prefetch;
        _feedback = feedback;
        _settings = settings;
        _state = state;
        _dispatcher = dispatcher;
        hook.InterceptFilter = ShouldIntercept;
        hook.PasteRequested += OnPasteRequested;
        hotkeys.Pressed += OnHotkey;
    }

    // Set when Smart Paste turns on, off, or pauses. Read on the hook thread.
    public bool IsActive
    {
        get => Volatile.Read(ref _active);
        set => Volatile.Write(ref _active, value);
    }

    // Hook thread, inside the keyboard hook: must be quick.
    private bool ShouldIntercept(IntPtr window) => IsActive && _profiles.HasArmed && _gate.ShouldIntercept(window);

    private async void OnPasteRequested(PasteRequest request)
    {
        var clock = Stopwatch.StartNew();
        var target = new PasteTarget(request.ForegroundWindow, request.WindowProcessId, request.WindowProcessId);
        var anchor = CursorAnchor();
        var record = new PasteRecord();
        var stillWanted = StillWanted(request);
        DispatcherTimer? working = null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Expired);
            var ct = linked.Token;
            if (_profiles.Current is not { } armed)
            {
                record.Reason = "no_profile";
                record.Pasted = await _paster.PastePlainAsync(target, stillWanted);
                return;
            }

            working = ShowWorkingLater(() => anchor);
            var field = await _focus.InspectAsync(InspectBudget, ct);
            if (field is null)
            {
                // Not an editable text field (a document, a terminal, a list): Ctrl+V does its usual thing.
                record.Reason = "not_a_field";
                record.Pasted = await _paster.PastePlainAsync(target, stillWanted);
                return;
            }

            target = target with { FocusProcessId = field.ProcessId };
            anchor = CaretLocator.Locate(request.ForegroundWindow, field.Bounds);
            record.App = field.Context.AppName;
            var (decision, context) = await _prefetch.DecideForPasteAsync(field, armed.Profile, ct);
            StopWorking(ref working);
            record.Label = context.BestLabel();
            record.Source = decision.Source;
            record.Reason = decision.Reason;
            record.DecidedBy = Map(decision.DecidedBy);

            switch (decision.Kind)
            {
                case DecisionKind.Value when decision.Value is { } value:
                    record.Key = decision.Key;
                    await PasteValueAsync(value, field, target, record, ct, stillWanted);
                    break;

                case DecisionKind.Picker:
                    var picked = await _feedback.PickAsync(
                        new PickRequest(decision.Candidates, PickMode.Ambiguous, anchor, record.Label), ct);
                    await ApplyPickAsync(picked, field, context, armed.Profile, target, record, PickMode.Ambiguous, ct, stillWanted);
                    break;

                default:
                    record.Pasted = await _paster.PastePlainAsync(target, stillWanted);
                    record.Outcome = ActivityOutcome.PastedOriginal;
                    break;
            }

            ConfirmPaste(record, anchor);
        }
        catch (OperationCanceledException) when (request.IsExpired)
        {
            // The safety timeout already gave the keyboard back: pasting now would land out of order.
            record.Reason = "expired";
        }
        catch (Exception ex)
        {
            Log.Error("paste_error", ex);
            record.Reason = "error";
            if (!record.Pasted && !request.IsExpired) record.Pasted = await _paster.PastePlainAsync(target, stillWanted);
        }
        finally
        {
            StopWorking(ref working);
            request.Complete();
            Finish(record, clock);
        }
    }

    // The picker shortcut: every value of the armed block, wherever the caret is.
    private async void OnHotkey(string name)
    {
        if (name != HotkeyNames.Picker || !IsActive || _picking) return;
        var window = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (window == IntPtr.Zero || !_gate.ShouldIntercept(window)) return;

        _picking = true;
        var clock = Stopwatch.StartNew();
        var record = new PasteRecord { Source = "user", Reason = "shortcut" };
        PasteRequest? hold = null;
        try
        {
            // Windows delivers the shortcut as a hot key, so unlike after Ctrl+V nothing holds the keys typed next: a
            // Tab typed right after choosing would move the focus before the value lands. From here on keys wait for
            // the pick to be pasted, as after a Ctrl+V. Null when they can't be held (another paste is in flight).
            hold = await _hook.HoldKeysAsync();
            var ct = hold?.Expired ?? CancellationToken.None;
            var stillWanted = StillWanted(hold);
            var field = await _focus.InspectAsync(InspectBudget, ct);
            var anchor = field is null ? CaretLocator.Locate(window, null) : CaretLocator.Locate(window, field.Bounds);
            record.Label = field?.Context.BestLabel() ?? "";
            record.App = field?.Context.AppName;
            if (_profiles.Current is not { } armed)
            {
                record.Reason = "nothing_armed";
                _feedback.ShowNotice(anchor, "Copy a block of details first, then press this again.");
                return;
            }

            var candidates = FieldDecider.AllCandidates(armed.Profile, field?.Context);
            var picked = await _feedback.PickAsync(new PickRequest(candidates, PickMode.AllValues, anchor, field?.Context.BestLabel()), ct);
            var target = new PasteTarget(window, processId, field?.ProcessId ?? processId);
            await ApplyPickAsync(picked, field, field?.Context, armed.Profile, target, record, PickMode.AllValues, ct, stillWanted);
            ConfirmPaste(record, anchor);
        }
        catch (OperationCanceledException) when (hold?.IsExpired == true)
        {
            // The safety timeout already gave the keyboard back: pasting now would land out of order.
            record.Reason = "expired";
        }
        catch (Exception ex)
        {
            Log.Error("picker_shortcut_error", ex);
        }
        finally
        {
            // After the paste, and so after the user's clipboard is back.
            hold?.Complete();
            _picking = false;
            Finish(record, clock);
        }
    }

    // Nothing may reach the target once the hook has given the held keys back: it would land after them. Null (no
    // check) when no keys are held.
    private static Func<bool>? StillWanted(PasteRequest? request) =>
        request is { } held ? () => !held.IsExpired : null;

    private async Task ApplyPickAsync(PickResult picked, FocusedField? field, FieldContext? context, Profile profile,
        PasteTarget target, PasteRecord record, PickMode mode, CancellationToken ct, Func<bool>? stillWanted)
    {
        switch (picked.Outcome)
        {
            case PickOutcome.Chosen when picked.Candidate is { } chosen:
                record.Key = chosen.Key;
                record.DecidedBy = DecisionMaker.User;
                if (context is not null) _decisions.LearnChoice(context, chosen, profile);
                await PasteValueAsync(chosen.Value, field, target, record, ct, stillWanted);
                break;
            // Escape in the Ctrl+V picker means "just paste what I copied". The shortcut's picker was never a paste,
            // so there it only closes.
            case PickOutcome.PasteOriginal when mode == PickMode.Ambiguous:
                record.Pasted = await _paster.PastePlainAsync(target, stillWanted);
                record.Outcome = ActivityOutcome.PastedOriginal;
                break;
            default:
                record.Reason = "picker_dismissed";
                record.Outcome = ActivityOutcome.Skipped;
                break;
        }
    }

    // Pastes a value into the field decided on, if focus is still there: a click into another field while Jev was
    // deciding (or while the picker was open) must not send the value there.
    private async Task PasteValueAsync(string value, FocusedField? field, PasteTarget target, PasteRecord record,
        CancellationToken ct, Func<bool>? stillWanted)
    {
        if (field is not null && await FocusMovedAsync(field, ct) is { } reason)
        {
            record.Reason = reason;
            record.Outcome = ActivityOutcome.Skipped;
            return;
        }
        var outcome = await _paster.PasteValueAsync(value, target, ct, stillWanted);
        record.Pasted = outcome is PasteOutcome.Consumed or PasteOutcome.NotConsumed or PasteOutcome.Superseded;
        if (outcome == PasteOutcome.TargetChanged) record.Reason = "target_changed";
        record.Outcome = record.Pasted ? ActivityOutcome.Pasted : ActivityOutcome.Skipped;
    }

    // Null while focus is still on the field; otherwise why the value must not be pasted: focus_moved, or focus_unknown
    // when UI Automation could not say in time (pasting blind could land in another field).
    private async Task<string?> FocusMovedAsync(FocusedField field, CancellationToken ct)
    {
        var now = await _focus.FocusedRuntimeIdAsync(RecheckBudget, ct);
        if (now is null) return "focus_unknown";
        return now.AsSpan().SequenceEqual(field.RuntimeId) ? null : "focus_moved";
    }

    private void ConfirmPaste(PasteRecord record, ScreenRect anchor)
    {
        if (record.Pasted && record.Outcome == ActivityOutcome.Pasted && record.Key is { } key && _settings.Current.ShowPasteConfirmation)
            _feedback.ShowPasted(anchor, FieldKeys.DisplayName(key));
    }

    // The "working" chip appears only when a decision takes longer than a moment.
    private DispatcherTimer ShowWorkingLater(Func<ScreenRect> anchor)
    {
        var timer = new DispatcherTimer(WorkingDelay, DispatcherPriority.Normal, (sender, _) =>
        {
            ((DispatcherTimer)sender!).Stop();
            _feedback.ShowWorking(anchor());
        }, _dispatcher);
        return timer;
    }

    private void StopWorking(ref DispatcherTimer? working)
    {
        if (working is null) return;
        working.Stop();
        working = null;
        _feedback.HideWorking();
    }

    private void Finish(PasteRecord record, Stopwatch clock)
    {
        // Extra keys come from labels in the copied text, so only standard key names are logged.
        var loggedKey = record.Key is null ? null : FieldKeys.Standard.Contains(record.Key) ? record.Key : "extra";
        Log.Event("paste", ("source", record.Source), ("reason", record.Reason), ("key", loggedKey),
            ("outcome", record.Outcome), ("app", record.App), ("ms", clock.ElapsedMilliseconds));
        if (record.Outcome is { } outcome && record.Label is { Length: > 0 } label)
            _state.AddActivity(new ActivityEntry(DateTimeOffset.Now, label, record.Key is { } key ? FieldKeys.DisplayName(key) : null,
                outcome, record.DecidedBy, record.App ?? ""));
    }

    private static ScreenRect CursorAnchor() => CaretLocator.Locate(IntPtr.Zero, null);

    private static DecisionMaker? Map(DecisionSource source) => source switch
    {
        DecisionSource.Jev => DecisionMaker.Jev,
        DecisionSource.Learned => DecisionMaker.Learned,
        DecisionSource.BuiltIn or DecisionSource.Rules => DecisionMaker.BuiltIn,
        _ => null
    };

    public void Dispose()
    {
        _hook.PasteRequested -= OnPasteRequested;
        _hook.InterceptFilter = null;
        _hotkeys.Pressed -= OnHotkey;
    }

    // What one paste did, for the log (tokens only) and the flyout's Recent list (in memory only).
    private sealed class PasteRecord
    {
        public string Source { get; set; } = "none";
        public string Reason { get; set; } = "none";
        public string? Key { get; set; }
        public string? Label { get; set; }
        public string? App { get; set; }
        public bool Pasted { get; set; }
        public ActivityOutcome? Outcome { get; set; }
        public DecisionMaker? DecidedBy { get; set; }
    }
}

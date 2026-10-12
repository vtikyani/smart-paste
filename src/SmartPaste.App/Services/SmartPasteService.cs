using System.Diagnostics;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// What Ctrl+V does while a block is armed, and what the picker shortcut does. Every Ctrl+V the keyboard hook hands
// over ends in exactly one of: the value the field wants, a normal paste, or (when the user dismissed the picker)
// nothing. Keys typed meanwhile are held by the hook and replayed in order once the request is completed.
//
// UI thread: every member, except ShouldIntercept, which the hook calls on its own thread.
internal sealed class SmartPasteService : IDisposable
{
    private static readonly TimeSpan InspectBudget = TimeSpan.FromMilliseconds(600);
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
        DispatcherTimer? working = null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Expired);
            var ct = linked.Token;
            if (_profiles.Current is not { } armed)
            {
                record.Reason = "no_profile";
                record.Pasted = _paster.PastePlain(target);
                return;
            }

            working = ShowWorkingLater(() => anchor);
            var field = await _focus.InspectAsync(InspectBudget, ct);
            if (field is null)
            {
                // Not an editable text field (a document, a terminal, a list): Ctrl+V does its usual thing.
                record.Reason = "not_a_field";
                record.Pasted = _paster.PastePlain(target);
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
                    record.Pasted = await PasteValueAsync(value, target, request.Expired, request.Complete);
                    record.Outcome = record.Pasted ? ActivityOutcome.Pasted : ActivityOutcome.Skipped;
                    break;

                case DecisionKind.Picker:
                    var picked = await _feedback.PickAsync(
                        new PickRequest(decision.Candidates, PickMode.Ambiguous, anchor, record.Label), ct);
                    await ApplyPickAsync(picked, context, target, record, request.Expired, request.Complete);
                    break;

                default:
                    record.Pasted = _paster.PastePlain(target);
                    record.Outcome = ActivityOutcome.PastedOriginal;
                    break;
            }

            if (record.Pasted && record.Outcome == ActivityOutcome.Pasted && record.Key is { } key && _settings.Current.ShowPasteConfirmation)
                _feedback.ShowPasted(anchor, FieldKeys.DisplayName(key));
        }
        catch (OperationCanceledException) when (request.Expired.IsCancellationRequested)
        {
            // The safety timeout already gave the keyboard back: pasting now would land out of order.
            record.Reason = "expired";
        }
        catch (Exception ex)
        {
            Log.Error("paste_error", ex);
            record.Reason = "error";
            if (!record.Pasted && !request.Expired.IsCancellationRequested) record.Pasted = _paster.PastePlain(target);
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
        try
        {
            var field = await _focus.InspectAsync(InspectBudget, CancellationToken.None);
            var anchor = field is null ? CaretLocator.Locate(window, null) : CaretLocator.Locate(window, field.Bounds);
            record.Label = field?.Context.BestLabel() ?? "";
            record.App = field?.Context.AppName;
            // With nothing armed the picker opens empty and says how to fill it.
            IReadOnlyList<Candidate> candidates = _profiles.Current is { } armed ? FieldDecider.AllCandidates(armed.Profile, field?.Context) : [];
            var picked = await _feedback.PickAsync(new PickRequest(candidates, PickMode.AllValues, anchor, field?.Context.BestLabel()), CancellationToken.None);
            var target = new PasteTarget(window, processId, field?.ProcessId ?? processId);
            await ApplyPickAsync(picked, field?.Context, target, record, CancellationToken.None, delivered: null);
            if (record.Pasted && record.Key is { } key && _settings.Current.ShowPasteConfirmation)
                _feedback.ShowPasted(anchor, FieldKeys.DisplayName(key));
        }
        catch (Exception ex)
        {
            Log.Error("picker_shortcut_error", ex);
        }
        finally
        {
            _picking = false;
            Finish(record, clock);
        }
    }

    private async Task ApplyPickAsync(PickResult picked, FieldContext? context, PasteTarget target, PasteRecord record,
        CancellationToken ct, Action? delivered)
    {
        switch (picked.Outcome)
        {
            case PickOutcome.Chosen when picked.Candidate is { } chosen:
                record.Key = chosen.Key;
                record.DecidedBy = DecisionMaker.User;
                if (context is not null) _decisions.LearnChoice(context, chosen);
                record.Pasted = await PasteValueAsync(chosen.Value, target, ct, delivered);
                record.Outcome = record.Pasted ? ActivityOutcome.Pasted : ActivityOutcome.Skipped;
                break;
            case PickOutcome.PasteOriginal:
                record.Pasted = _paster.PastePlain(target);
                record.Outcome = ActivityOutcome.PastedOriginal;
                break;
            default:
                record.Reason = "picker_dismissed";
                record.Outcome = ActivityOutcome.Skipped;
                break;
        }
    }

    private async Task<bool> PasteValueAsync(string value, PasteTarget target, CancellationToken ct, Action? delivered)
    {
        var outcome = await _paster.PasteValueAsync(value, target, ct, delivered);
        return outcome is PasteOutcome.Consumed or PasteOutcome.NotConsumed or PasteOutcome.Superseded;
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
        Log.Event("paste", ("source", record.Source), ("reason", record.Reason), ("key", record.Key),
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

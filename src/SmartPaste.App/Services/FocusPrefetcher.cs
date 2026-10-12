using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// Asks ahead. While a block is armed, the field that gets focus is decided right away, so the answer is usually ready
// before Ctrl+V and the paste feels instant. With field hints on, the answer also shows next to the field. With
// "Ask Jev ahead" off, hints come from the local label list and Jev is asked only at paste time.
//
// UI thread: every member except the decision tasks themselves.
internal sealed class FocusPrefetcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AheadDeadline = TimeSpan.FromSeconds(4);
    // Reading a label off the screen is local and usually takes a few hundred milliseconds; it must never hold a
    // paste up for long.
    private static readonly TimeSpan OcrDeadline = TimeSpan.FromMilliseconds(700);
    private const int PreviewLength = 28;

    private readonly FocusInspector _focus;
    private readonly InterceptGate _gate;
    private readonly WindowsOcr _ocr;
    private readonly DecisionService _decisions;
    private readonly ProfileStore _profiles;
    private readonly SettingsStore _settings;
    private readonly IPasteFeedback _feedback;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly Dictionary<string, Ahead> _ahead = [];

    private CancellationTokenSource _work = new();
    private Profile? _profile;
    private FocusedField? _focused;
    private bool _watching;

    public FocusPrefetcher(FocusInspector focus, InterceptGate gate, WindowsOcr ocr, DecisionService decisions,
        ProfileStore profiles, SettingsStore settings, IPasteFeedback feedback, Dispatcher dispatcher)
    {
        _focus = focus;
        _gate = gate;
        _ocr = ocr;
        _decisions = decisions;
        _profiles = profiles;
        _settings = settings;
        _feedback = feedback;
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer(Debounce, DispatcherPriority.Background, (_, _) => OnSettled(), dispatcher);
        _debounce.Stop();
        focus.FocusChanged += OnFocusChanged;
        profiles.Changed += OnProfileChanged;
        settings.Changed += OnSettingsChanged;
        CredentialStore.Changed += OnKeysChanged;
    }

    // Starts or stops watching focus to match the app's state. Call after the app turns on, off, or pauses.
    public void Refresh()
    {
        var settings = _settings.Current;
        var armed = _profiles.Current;
        var wanted = armed is not null && settings.IsActiveAt(DateTimeOffset.Now) && (settings.AskJevAhead || settings.ShowFieldHints);

        if (!ReferenceEquals(armed?.Profile, _profile))
        {
            // A new block (or none): earlier answers were about other values.
            Forget();
            _profile = armed?.Profile;
            if (wanted && _focused is not null) _debounce.Start();
        }

        if (wanted == _watching) return;
        _watching = wanted;
        if (wanted)
        {
            _focus.StartWatchingFocus();
        }
        else
        {
            _focus.StopWatchingFocus();
            _debounce.Stop();
            _focused = null;
            Forget();
        }
    }

    // Paste time: the answer asked ahead for this field when there is one, else a fresh decision. Also returns the
    // field as decided (with an OCR label when it had none), which the picker learns from.
    public async Task<(Decision Decision, FieldContext Context)> DecideForPasteAsync(FocusedField field, Profile profile, CancellationToken ct)
    {
        if (_ahead.TryGetValue(Key(field), out var ahead) && ReferenceEquals(ahead.Profile, profile) && ahead.UsedJev)
        {
            try
            {
                var decided = await ahead.Decision.WaitAsync(FieldDecider.PasteDeadline, ct);
                // An answer made while Jev could not be reached came from the local list: ask again now.
                if (decided.Decision.JevFailure is null)
                {
                    Log.Event("ask_ahead_used", ("source", decided.Decision.Source));
                    return decided;
                }
            }
            catch (TimeoutException)
            {
                // Jev is still thinking about this field: the paste must not wait longer, so decide locally.
                var context = (await WithLabelAsync(field, ct)).Context;
                return (await _decisions.DecideAsync(context, profile, FieldDecider.PasteDeadline, ct, allowJev: false), context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The earlier attempt failed; decide afresh below.
            }
        }

        var labeled = await WithLabelAsync(field, ct);
        return (await _decisions.DecideAsync(labeled.Context, profile, FieldDecider.PasteDeadline, ct), labeled.Context);
    }

    private void OnFocusChanged(FocusedField? field)
    {
        _focused = field;
        _feedback.HideFieldHint();
        _debounce.Stop();
        if (field is not null && _watching) _debounce.Start();
    }

    // Focus has rested on a field for a moment: decide it, and show the hint.
    private async void OnSettled()
    {
        _debounce.Stop();
        if (_focused is not { } field || _profiles.Current is not { } armed || !_gate.ShouldIntercept(NativeMethods.GetForegroundWindow()))
            return;
        var key = Key(field);
        if (!_ahead.TryGetValue(key, out var ahead) || !ReferenceEquals(ahead.Profile, armed.Profile))
        {
            ahead = Start(field, armed.Profile);
            _ahead[key] = ahead;
        }

        try
        {
            var (decision, _) = await ahead.Decision;
            if (_settings.Current.ShowFieldHints && _focused is { } now && Key(now) == key && ReferenceEquals(_profile, ahead.Profile))
                ShowHint(field, decision);
        }
        catch (OperationCanceledException)
        {
            // The block changed or Smart Paste stopped watching.
        }
        catch (Exception ex)
        {
            Log.Error("ask_ahead_error", ex);
        }
    }

    private Ahead Start(FocusedField field, Profile profile)
    {
        var useJev = _settings.Current.AskJevAhead;
        var token = _work.Token;
        return new Ahead(profile, useJev, DecideAheadAsync(field, profile, useJev, token));
    }

    private async Task<(Decision, FieldContext)> DecideAheadAsync(FocusedField field, Profile profile, bool useJev, CancellationToken ct)
    {
        var labeled = await WithLabelAsync(field, ct);
        var decision = await _decisions.DecideAsync(labeled.Context, profile, AheadDeadline, ct, allowJev: useJev);
        Log.Event("ask_ahead", ("source", decision.Source), ("reason", decision.Reason));
        return (decision, labeled.Context);
    }

    // A field that names itself nowhere UI Automation can see gets its label read from the screen.
    private async Task<FocusedField> WithLabelAsync(FocusedField field, CancellationToken ct)
    {
        var context = field.Context;
        if (context.IsPassword || context.HasUsableLabel || !_ocr.IsAvailable) return field;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(OcrDeadline);
        try
        {
            var label = await _ocr.ReadLabelNearAsync(field.Bounds, limit.Token);
            return string.IsNullOrWhiteSpace(label) ? field : field with { Context = context with { OcrText = label } };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Event("ocr_label_timeout");
            return field;
        }
    }

    private void ShowHint(FocusedField field, Decision decision)
    {
        switch (decision.Kind)
        {
            case DecisionKind.Value when decision.Key is { } key && decision.Value is { } value:
                _feedback.ShowFieldHint(field.Bounds, new FieldHint(FieldKeys.DisplayName(key), Preview(value), NeedsChoice: false));
                break;
            case DecisionKind.Picker when decision.Candidates.Count > 0:
                var name = decision.Key is { } shared ? FieldKeys.DisplayName(shared) : "Choose a value";
                _feedback.ShowFieldHint(field.Bounds, new FieldHint(name, "", NeedsChoice: true));
                break;
            default:
                _feedback.HideFieldHint();
                break;
        }
    }

    private static string Preview(string value)
    {
        var flat = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= PreviewLength ? flat : flat[..(PreviewLength - 1)].TrimEnd() + "…";
    }

    private static string Key(FocusedField field) => string.Join('.', field.RuntimeId);

    private void OnProfileChanged() => _dispatcher.BeginInvoke(Refresh);

    // New keys: answers made without Jev (or with the old key) no longer stand.
    private void OnKeysChanged(string target) => _dispatcher.BeginInvoke(() =>
    {
        Forget();
        if (_watching && _focused is not null) _debounce.Start();
    });

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (previous.AskJevAhead != current.AskJevAhead) Forget();
            if (!current.ShowFieldHints) _feedback.HideFieldHint();
            Refresh();
        });
    }

    private void Forget()
    {
        _work.Cancel();
        _work.Dispose();
        _work = new CancellationTokenSource();
        _ahead.Clear();
        _feedback.HideFieldHint();
    }

    public void Dispose()
    {
        _debounce.Stop();
        _focus.FocusChanged -= OnFocusChanged;
        _profiles.Changed -= OnProfileChanged;
        _settings.Changed -= OnSettingsChanged;
        CredentialStore.Changed -= OnKeysChanged;
        if (_watching) _focus.StopWatchingFocus();
        _work.Cancel();
        _work.Dispose();
    }

    // One field's answer, asked ahead for a given block. UsedJev false means the local label list decided (hints
    // only); the paste then asks Jev itself.
    private sealed record Ahead(Profile Profile, bool UsedJev, Task<(Decision Decision, FieldContext Context)> Decision);
}

using System.Windows.Threading;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Popups;

// What the user sees around the field during a smart paste: the picker, the "Thinking" and "pasted" chips, and the
// field hint. Every window exists from startup and stays hidden, so showing one is instant. It shows what it is
// asked to; the paste service decides when (settings, delays before "Thinking").
//
// Construct on the UI thread. The methods may be called from any thread: they run on the UI thread, and
// PickAsync completes there.
internal sealed class PasteFeedback : IPasteFeedback, IDisposable
{
    public static readonly TimeSpan DefaultPickerTimeout = TimeSpan.FromSeconds(20);

    // A hint appears only once the user settles on a field, so tabbing through a form does not flicker.
    private static readonly TimeSpan HintDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan PastedHold = TimeSpan.FromMilliseconds(1000);
    // A notice is read, not glanced at: it stays a little longer.
    private static readonly TimeSpan NoticeHold = TimeSpan.FromMilliseconds(2200);
    private static readonly TimeSpan QuickFade = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan SoftFade = TimeSpan.FromMilliseconds(220);

    private readonly IKeyCapture _keyCapture;
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _pickerTimeout;
    private readonly PickerWindow _picker;
    private readonly ChipWindow _working;
    private readonly ChipWindow _pasted;
    private readonly ChipWindow _hint;
    private readonly DispatcherTimer _pastedTimer;
    private readonly DispatcherTimer _hintTimer;
    private PickSession? _session;
    private (ScreenRect Field, FieldHint Hint)? _pendingHint;
    private ScreenRect? _hintField;
    private bool _disposed;

    public PasteFeedback(IKeyCapture keyCapture, TimeSpan? pickerTimeout = null)
    {
        _keyCapture = keyCapture;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _pickerTimeout = pickerTimeout ?? DefaultPickerTimeout;
        _picker = new PickerWindow(new PickerViewModel());
        _working = new ChipWindow();
        _pasted = new ChipWindow();
        _hint = new ChipWindow();
        _pastedTimer = new DispatcherTimer(PastedHold, DispatcherPriority.Normal, (_, _) => EndPasted(), _dispatcher) { IsEnabled = false };
        _hintTimer = new DispatcherTimer(HintDelay, DispatcherPriority.Normal, (_, _) => ShowPendingHint(), _dispatcher) { IsEnabled = false };
    }

    // The picker window, for screenshots and tests.
    internal PickerWindow Picker => _picker;

    // Creates the native windows ahead of time.
    public void Prepare()
    {
        _picker.Prepare();
        _working.Prepare();
        _pasted.Prepare();
        _hint.Prepare();
    }

    public void ShowWorking(ScreenRect anchor) => OnUiThread(() =>
    {
        if (_session is null) _working.ShowWorking(anchor);
    });

    public void HideWorking() => OnUiThread(() => _working.Dismiss(QuickFade));

    public void ShowPasted(ScreenRect anchor, string what) => OnUiThread(() =>
    {
        _working.Dismiss(TimeSpan.Zero);
        _pasted.ShowPasted(anchor, what);
        _pastedTimer.Stop();
        _pastedTimer.Interval = PastedHold;
        _pastedTimer.Start();
    });

    // Shares the "pasted" chip: the two never need to show at once.
    public void ShowNotice(ScreenRect anchor, string message) => OnUiThread(() =>
    {
        _working.Dismiss(TimeSpan.Zero);
        _pasted.ShowNotice(anchor, message);
        _pastedTimer.Stop();
        _pastedTimer.Interval = NoticeHold;
        _pastedTimer.Start();
    });

    public void ShowFieldHint(ScreenRect field, FieldHint hint) => OnUiThread(() =>
    {
        if (_session is not null) return;
        if (_hint.IsShowing && _hintField == field)
        {
            // Same field, newer answer (Jev replied): update in place.
            _hint.ShowHint(field, hint, animate: false);
            return;
        }
        if (_hint.IsShowing) _hint.Dismiss(TimeSpan.Zero);
        _hintField = null;
        _pendingHint = (field, hint);
        _hintTimer.Stop();
        _hintTimer.Start();
    });

    public void HideFieldHint() => OnUiThread(() => ClearHint(QuickFade));

    public Task<PickResult> PickAsync(PickRequest request, CancellationToken ct)
    {
        if (!_dispatcher.CheckAccess())
            return _dispatcher.InvokeAsync(() => PickAsync(request, ct)).Task.Unwrap();
        if (_disposed || ct.IsCancellationRequested || request.Candidates.Count == 0)
            return Task.FromResult(PickResult.Dismissed);

        // A new request replaces one still open, and nothing else should sit next to the picker.
        _session?.Finish(PickResult.Dismissed);
        _working.Dismiss(TimeSpan.Zero);
        ClearHint(TimeSpan.Zero);

        var session = new PickSession(_picker, request, _pickerTimeout, _dispatcher);
        session.Finished += (_, _) =>
        {
            if (_session == session) _session = null;
        };
        _session = session;
        session.Start(_keyCapture, ct);
        return session.Result;
    }

    private void EndPasted()
    {
        _pastedTimer.Stop();
        _pasted.Dismiss(SoftFade);
    }

    private void ShowPendingHint()
    {
        _hintTimer.Stop();
        if (_pendingHint is not { } pending || _session is not null) return;
        _pendingHint = null;
        _hintField = pending.Field;
        _hint.ShowHint(pending.Field, pending.Hint, animate: true);
    }

    private void ClearHint(TimeSpan fade)
    {
        _hintTimer.Stop();
        _pendingHint = null;
        _hintField = null;
        _hint.Dismiss(fade);
    }

    private void OnUiThread(Action action)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session?.Finish(PickResult.Dismissed);
        _disposed = true;
        _pastedTimer.Stop();
        _hintTimer.Stop();
        _picker.Close();
        _working.Close();
        _pasted.Close();
        _hint.Close();
    }
}

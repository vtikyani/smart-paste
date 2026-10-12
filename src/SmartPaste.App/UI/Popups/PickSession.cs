using System.Collections.Concurrent;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Popups;

// One open picker. Turns key presses from the keyboard hook, clicks, a timeout, and cancellation into exactly one
// PickResult. Accepts runs on the hook thread and touches only fields fixed at construction, volatile fields, and
// its own counter; everything else runs on the UI thread.
internal sealed class PickSession : IKeySink
{
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkCapital = 0x14;
    private const int VkEscape = 0x1B;
    private const int VkPrior = 0x21;
    private const int VkNext = 0x22;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const int Vk1 = 0x31;
    private const int Vk9 = 0x39;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkNumpad1 = 0x61;
    private const int VkNumpad9 = 0x69;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;
    private const int VkLeftShift = 0xA0;
    private const int VkRightMenu = 0xA5;

    private readonly PickerWindow _picker;
    private readonly PickRequest _request;
    private readonly TimeSpan _timeout;
    private readonly Dispatcher _dispatcher;
    private readonly bool _filterable;
    private readonly TaskCompletionSource<PickResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // One entry per key that went through to the app, in order: whether it was a modifier. OnOtherKey pairs with
    // them, so pressing Shift (before a capital letter) or Caps Lock does not close the picker.
    private readonly ConcurrentQueue<bool> _passedThrough = new();
    private volatile bool _finished;
    // Set on the hook thread by the key that ends the session, before the UI thread has handled it: every key after it
    // goes to the app.
    private volatile bool _closing;
    // The rows on screen, published by the UI thread whenever the list changes, so the hook thread can tell whether a
    // number key picks a row.
    private volatile int _rowCount;
    // Accepted keys are handed to OnKey one by one, in order. The hook thread numbers them (hook thread only) and
    // records the number of the one that ends the session; the UI thread counts them as they arrive.
    private int _acceptedKeys;
    private volatile int _closingKey;
    private int _handledKeys;
    private DispatcherTimer? _timer;
    private IDisposable? _capture;
    private CancellationTokenRegistration _cancellation;

    public PickSession(PickerWindow picker, PickRequest request, TimeSpan timeout, Dispatcher dispatcher)
    {
        _picker = picker;
        _request = request;
        _timeout = timeout;
        _dispatcher = dispatcher;
        _filterable = request.Mode == PickMode.AllValues;
    }

    public Task<PickResult> Result => _result.Task;

    // Raised once, on the UI thread, when the session ends for any reason.
    public event EventHandler? Finished;

    public void Start(IKeyCapture keyCapture, CancellationToken ct)
    {
        _picker.ViewModel.Load(_request);
        _rowCount = _picker.ViewModel.Rows.Count;
        _picker.RowClicked += OnRowClicked;
        _picker.Present(_request.Anchor);
        _timer = new DispatcherTimer(_timeout, DispatcherPriority.Normal, (_, _) => Finish(PickResult.Dismissed), _dispatcher);
        try
        {
            _capture = keyCapture.Capture(this);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever the hook's trouble, the picker still works with the mouse and the timeout closes it.
            Log.Error("picker_capture_error", ex);
        }
        if (ct.CanBeCanceled)
            _cancellation = ct.Register(() => _dispatcher.BeginInvoke(() => Finish(PickResult.Dismissed)));
    }

    public void Finish(PickResult result)
    {
        if (_finished) return;
        _finished = true;
        _timer?.Stop();
        _capture?.Dispose();
        _capture = null;
        _cancellation.Dispose();
        _picker.RowClicked -= OnRowClicked;
        // A choice is pasted at once, so the picker gets out of the way at once; anything else fades.
        _picker.Dismiss(immediately: result.Outcome == PickOutcome.Chosen);
        _result.TrySetResult(result);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    // Hook thread: decides in microseconds, without locks. The session ends on the UI thread, a moment after the key
    // that ends it was accepted here; a key typed in that moment must not be swallowed and then dropped. So the key
    // that ends the session (Escape, Enter on a row, a row's number) closes it here at once, and every key after it
    // goes to the app, in order.
    public bool Accepts(in KeyStroke key)
    {
        var accepted = !_closing && !_finished && Wanted(key);
        if (!accepted)
        {
            _passedThrough.Enqueue(IsModifier(key.VirtualKey));
            return false;
        }
        var number = ++_acceptedKeys;
        if (Ends(key))
        {
            _closingKey = number;
            _closing = true;
        }
        return true;
    }

    public void OnKey(KeyStroke key)
    {
        if (_finished) return;
        var closing = ++_handledKeys == _closingKey;
        Handle(key);
        // The hook judged this key to end the picker from the list as it was; a filter typed a moment before can have
        // changed that (no such row any more). The keys after it went to the app already, so end it all the same.
        if (closing && !_finished) Finish(PickResult.Dismissed);
    }

    private void Handle(KeyStroke key)
    {
        RestartTimeout();
        var list = _picker.ViewModel;
        switch (key.VirtualKey)
        {
            case VkEscape:
                // After Ctrl+V, Escape pastes what was copied, as if Smart Paste weren't there. The picker shortcut
                // was no paste at all, so there Escape only closes.
                Finish(_request.Mode == PickMode.AllValues ? PickResult.Dismissed : PickResult.Original);
                return;
            case VkReturn:
                if (list.Selected is { } selected) Finish(PickResult.Chose(selected.Candidate));
                return;
            case >= Vk1 and <= Vk9 when key.Modifiers == KeyModifiers.None:
                Choose(list.RowForNumber(key.VirtualKey - Vk1 + 1));
                return;
            case >= VkNumpad1 and <= VkNumpad9 when (key.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0:
                Choose(list.RowForNumber(key.VirtualKey - VkNumpad1 + 1));
                return;
            case VkUp:
                list.Move(-1);
                break;
            case VkDown:
                list.Move(1);
                break;
            case VkPrior:
                list.Move(-5);
                break;
            case VkNext:
                list.Move(5);
                break;
            case VkHome:
                list.MoveToEnd(last: false);
                break;
            case VkEnd:
                list.MoveToEnd(last: true);
                break;
            case VkBack:
                list.Backspace();
                _rowCount = list.Rows.Count;
                _picker.Refit(_request.Anchor);
                break;
            default:
                if (key.Character is { } character)
                {
                    list.Type(character);
                    _rowCount = list.Rows.Count;
                    _picker.Refit(_request.Anchor);
                }
                break;
        }
        _picker.RevealSelection();
    }

    public void OnOtherKey()
    {
        if (_passedThrough.TryDequeue(out var wasModifier) && wasModifier) return;
        Finish(PickResult.Dismissed);
    }

    public void OnPointerDown(int x, int y)
    {
        if (!_picker.Contains(x, y)) Finish(PickResult.Dismissed);
    }

    // Number keys choose (a number with no row is ignored), arrows and Enter navigate, Escape closes (after Ctrl+V,
    // pasting the original). In "all values" mode, Backspace and printable characters filter. Anything else, and any
    // shortcut with Ctrl, Alt, or Windows, goes to the app and closes the picker.
    private bool Wanted(in KeyStroke key)
    {
        const KeyModifiers Shortcut = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows;
        if ((key.Modifiers & Shortcut) != 0)
        {
            // AltGr arrives as Ctrl+Alt. On many layouts it types characters people filter with (@, €, {).
            var altGr = (key.Modifiers & Shortcut) == (KeyModifiers.Control | KeyModifiers.Alt);
            return altGr && _filterable && IsPrintable(key.Character);
        }
        switch (key.VirtualKey)
        {
            case VkEscape or VkReturn or VkUp or VkDown:
                return true;
            case VkPrior or VkNext or VkHome or VkEnd or VkBack:
                return _filterable;
            case >= Vk1 and <= Vk9 when key.Modifiers == KeyModifiers.None:
                return true;
            case >= VkNumpad1 and <= VkNumpad9:
                return true;
        }
        return _filterable && IsPrintable(key.Character);
    }

    // Hook thread, for a wanted key: whether handling it ends the session, by the same rules as Handle, judged from the
    // row count the UI thread published last.
    private bool Ends(in KeyStroke key) => key.VirtualKey switch
    {
        VkEscape => true,
        // Enter chooses the selected row; there is one whenever the list has rows.
        VkReturn => _rowCount > 0,
        >= Vk1 and <= Vk9 when key.Modifiers == KeyModifiers.None => IsRowNumber(key.VirtualKey - Vk1 + 1),
        >= VkNumpad1 and <= VkNumpad9 when (key.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0 =>
            IsRowNumber(key.VirtualKey - VkNumpad1 + 1),
        _ => false
    };

    private bool IsRowNumber(int number) => number <= Math.Min(_rowCount, PickerViewModel.Numbered);

    private static bool IsPrintable(char? character) => character is { } c && !char.IsControl(c);

    private static bool IsModifier(int virtualKey) =>
        virtualKey is VkShift or VkControl or VkMenu or VkCapital or VkLeftWindows or VkRightWindows or VkNumLock or VkScrollLock
        || virtualKey is >= VkLeftShift and <= VkRightMenu;

    private void Choose(PickerRow? row)
    {
        if (row is not null) Finish(PickResult.Chose(row.Candidate));
    }

    private void OnRowClicked(object? sender, PickerRow row) => Finish(PickResult.Chose(row.Candidate));

    private void RestartTimeout()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Start();
    }
}

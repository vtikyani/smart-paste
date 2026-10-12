using System.Collections.Concurrent;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Popups;

// One open picker. Turns key presses from the keyboard hook, clicks, a timeout, and cancellation into exactly one
// PickResult. Accepts runs on the hook thread and reads only fields fixed at construction or volatile; everything
// else runs on the UI thread.
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

    // Hook thread: decides in microseconds, touching nothing but immutable and volatile state.
    public bool Accepts(in KeyStroke key)
    {
        var accepted = !_finished && Wanted(key);
        if (!accepted) _passedThrough.Enqueue(IsModifier(key.VirtualKey));
        return accepted;
    }

    public void OnKey(KeyStroke key)
    {
        if (_finished) return;
        RestartTimeout();
        var list = _picker.ViewModel;
        switch (key.VirtualKey)
        {
            case VkEscape:
                Finish(PickResult.Original);
                return;
            case VkReturn:
                if (list.Selected is { } selected) Finish(PickResult.Chose(selected.Candidate));
                return;
            case >= Vk1 and <= Vk9:
                Choose(list.RowForNumber(key.VirtualKey - Vk1 + 1));
                return;
            case >= VkNumpad1 and <= VkNumpad9:
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
                _picker.Refit(_request.Anchor);
                break;
            default:
                if (key.Character is { } character)
                {
                    list.Type(character);
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

    // Number keys choose (a number with no row is ignored), arrows and Enter navigate, Escape pastes the original.
    // In "all values" mode, Backspace and printable characters filter. Anything else, and any shortcut with Ctrl,
    // Alt, or Windows, goes to the app and closes the picker.
    private bool Wanted(in KeyStroke key)
    {
        if ((key.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)) != 0) return false;
        switch (key.VirtualKey)
        {
            case VkEscape or VkReturn or VkUp or VkDown:
                return true;
            case VkPrior or VkNext or VkHome or VkEnd or VkBack:
                return _filterable;
            case >= Vk1 and <= Vk9:
                if ((key.Modifiers & KeyModifiers.Shift) == 0) return true;
                break;
            case >= VkNumpad1 and <= VkNumpad9:
                return true;
        }
        return _filterable && key.Character is { } c && !char.IsControl(c);
    }

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

namespace SmartPaste.App.Shell;

// A Ctrl+V the keyboard hook turned into a smart paste, or a hold for the picker shortcut (KeyboardHook.HoldKeysAsync).
// Every key typed after it is held back until Complete is called, then replayed in order. The owner must always
// complete it, whatever the outcome.
internal sealed class PasteRequest
{
    private readonly Action<PasteRequest> _onComplete;
    private readonly CancellationTokenSource _expired = new();
    private int _completed;
    private int _isExpired;

    internal PasteRequest(IntPtr foregroundWindow, uint windowProcessId, int sequence, long timestamp, Action<PasteRequest> onComplete)
    {
        ForegroundWindow = foregroundWindow;
        WindowProcessId = windowProcessId;
        Sequence = sequence;
        Timestamp = timestamp;
        _onComplete = onComplete;
    }

    // The window that had the foreground when the request was raised.
    public IntPtr ForegroundWindow { get; }

    public uint WindowProcessId { get; }

    // Increases with every request the hook raises.
    public int Sequence { get; }

    // Stopwatch.GetTimestamp() when the request was raised.
    public long Timestamp { get; }

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    // Canceled when the hook stopped waiting (safety timeout or shutdown) and released the held keys itself. A paste
    // after that would land after keys the user typed later, so the owner should give up.
    public CancellationToken Expired => _expired.Token;

    // True from the moment the hook gave up waiting, before the Expired token is canceled (which happens on the thread
    // pool). Check it right before sending anything to the target.
    public bool IsExpired => Volatile.Read(ref _isExpired) != 0;

    // Releases the keys typed since Ctrl+V. Safe to call more than once and from any thread.
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0) _onComplete(this);
    }

    // Hook thread: the hook released the keys without waiting for Complete. Token callbacks run on the thread pool,
    // never on the hook thread.
    internal void Expire()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        Volatile.Write(ref _isExpired, 1);
        ThreadPool.QueueUserWorkItem(static source => source.Cancel(), _expired, preferLocal: false);
    }
}

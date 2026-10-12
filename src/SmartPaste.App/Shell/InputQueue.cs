using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// What the type-ahead queue needs from the hook around it. Every member is called on the hook thread, several of
// them from inside the hook callback, so they must return quickly and never block.
internal interface IInputQueueHost
{
    // Whether a Ctrl+V should become a smart paste right now (a block is armed, the foreground app is allowed).
    bool ShouldIntercept();

    // The system's own view of a key (GetAsyncKeyState).
    bool IsKeyDownNow(ushort virtualKey);

    // The character a key types in the foreground app's keyboard layout, or null when it types none.
    char? TranslateCharacter(in KeyEvent key, KeyStateTracker keys);

    // A new paste request. Called inside the hook callback: hand it off, do not process it here.
    void RaiseRequest(int sequence);

    // The queue gave up waiting for this request (timeout, overflow, or shutdown) and released the held keys.
    void ExpireRequest(int sequence, string reason);

    // Send these keys in one batch, then the fence key carrying this id. Called inside the hook callback, so the
    // host must perform the injection after the callback has returned.
    void Inject(IReadOnlyList<KeyEvent> keys, byte fence);

    // The capture sink took a key press, or a key press went to the app instead (UI-thread delivery).
    void DeliverKey(IKeySink capture, KeyStroke stroke);

    void DeliverOtherKey(IKeySink capture);

    // A diagnostic event for the log; the host writes it off the hook thread.
    void Report(string eventName, int count, string? reason = null);
}

internal sealed record InputQueueOptions
{
    public static InputQueueOptions Default { get; } = new();

    // A request that is not completed within this time (picker time excluded) is completed by the queue itself, so
    // the keyboard can never stay held.
    public long RequestTimeoutMs { get; init; } = 4000;

    // How long to wait for a replayed batch to come back through the hook before moving on anyway.
    public long DrainTimeoutMs { get; init; } = 1000;

    // Pause between replaying the keys typed before a queued Ctrl+V and raising it, so the target app has handled
    // those keys (a Tab has moved focus) before the next field is inspected.
    public long SettleDelayMs { get; init; } = 100;

    // A pending request is released early once this many events are held.
    public int MaxBuffered { get; init; } = 512;
}

// The type-ahead buffer behind smart paste. From the moment a Ctrl+V becomes a paste request until the request is
// completed, every key event is held back; afterwards they are replayed in order, so "Ctrl+V, Tab, Ctrl+V" never
// lands a value in the wrong field. A Ctrl+V typed while a request is pending becomes the next request at its place
// in the stream.
//
// Pure state machine: no Win32, no clock, no threads. The keyboard hook feeds it events and timestamps on the hook
// thread and carries out what it asks for through IInputQueueHost.
internal sealed class InputQueue
{
    // A held key repeats at least this often: Windows' longest repeat delay is one second. Longer silence from a key
    // we are swallowing means its release never reached the hook.
    private const long MaxRepeatGapMs = 1500;

    private static readonly ushort[] ModifierKeys =
    [
        VirtualKeys.LeftShift, VirtualKeys.RightShift, VirtualKeys.LeftControl, VirtualKeys.RightControl,
        VirtualKeys.LeftMenu, VirtualKeys.RightMenu, VirtualKeys.LeftWindows, VirtualKeys.RightWindows
    ];

    private readonly IInputQueueHost _host;
    private readonly InputQueueOptions _options;
    private readonly KeyStateTracker _keys = new();
    private readonly List<Entry> _buffer = [];

    // Keys whose key-down started an accepted smart paste: their repeats and their release belong to that paste.
    private readonly HashSet<ushort> _suppressed = [];

    // Keys whose key-down a capture sink took: their repeats go to the sink and their release must not reach the app.
    private readonly HashSet<ushort> _captured = [];

    // Modifiers our own injected input released while the user still holds them. The system now reports them up;
    // that is not a missed key-up, so the repair step leaves them alone.
    private readonly HashSet<ushort> _releasedByUs = [];

    // Trigger keys of queued Ctrl+V presses that are not raised yet, and the marker they belong to.
    private readonly Dictionary<ushort, int> _queuedTriggers = [];

    // When each suppressed or captured key was last seen.
    private readonly Dictionary<ushort, long> _lastSeen = [];

    private Phase _phase;
    private Request? _active;
    private bool _captureActive;
    private int _lastSequence;
    private int _lastMarker;
    private byte _fence;
    private long _deadline;

    // lastSequence continues numbering from an earlier queue, so a completion meant for a request of the previous hook
    // session can never match a request of this one.
    public InputQueue(IInputQueueHost host, InputQueueOptions? options = null, int lastSequence = 0)
    {
        _host = host;
        _options = options ?? InputQueueOptions.Default;
        _lastSequence = lastSequence;
    }

    private enum Phase
    {
        // Keys pass straight through.
        Idle,

        // A request is being decided; keys are held.
        Pending,

        // A replay batch was injected; keys are held until its fence comes back through the hook.
        Draining,

        // A queued Ctrl+V is next; keys are held until the settle delay has passed.
        Settling
    }

    public bool IsIdle => _phase == Phase.Idle;

    public int HeldCount => _buffer.Count;

    // When Tick next has work to do, in the same clock as the timestamps passed in. Null when nothing is timed.
    public long? NextDeadline => _phase switch
    {
        Phase.Pending => _active?.Deadline(_options.RequestTimeoutMs),
        Phase.Draining or Phase.Settling => _deadline,
        _ => null
    };

    // Starts from the keys the system reports as held when the hook is installed.
    public void Seed(Func<ushort, bool> isDown)
    {
        for (ushort key = 1; key < 255; key++) _keys.Set(key, isDown(key));
    }

    // A key event that is not one of ours. Returns true when the hook must swallow it.
    public bool OnKey(in KeyEvent key, long now, IKeySink? capture)
    {
        var virtualKey = key.VirtualKey;
        if (!key.IsUp) ForgetIfReleaseWasMissed(virtualKey, now);
        if (_phase == Phase.Idle && !key.IsUp) RepairFromSystem(virtualKey);
        var isRepeat = !key.IsUp && _keys.IsDown(virtualKey);
        var swallow = Route(key, isRepeat, now, capture);
        _keys.Apply(key);
        _releasedByUs.Remove(virtualKey);
        return swallow;
    }

    // One of our own injected events came back through the hook. Returns true for fences, which no app may see.
    public bool OnOwnInjected(in KeyEvent key, long now)
    {
        if (key.VirtualKey == VirtualKeys.Fence)
        {
            if (_phase == Phase.Draining)
            {
                if (key.ScanCode == _fence) Advance(now);
                else _host.Report("type_ahead_fence_mismatch", key.ScanCode);
            }
            return true;
        }

        if (key.IsUp && VirtualKeys.IsModifier(key.VirtualKey) && _keys.IsDown(key.VirtualKey))
            _releasedByUs.Add(key.VirtualKey);
        return false;
    }

    // The host could not inject a batch; there is no fence to wait for.
    public void OnInjectionFailed(byte fence, long now)
    {
        if (_phase == Phase.Draining && fence == _fence) Advance(now);
    }

    // The owner of a request is done with it: the keys typed meanwhile can go.
    public void Complete(int sequence, long now)
    {
        if (_phase != Phase.Pending || _active?.Sequence != sequence) return;
        _active = null;
        Advance(now);
    }

    // The request clock stops while a capture (the picker) has the keyboard: choosing can take as long as it takes.
    public void SetCaptureActive(bool active, long now)
    {
        if (_captureActive == active) return;
        _captureActive = active;
        if (active) _active?.Pause(now);
        else _active?.Resume(now);
    }

    public void Tick(long now)
    {
        switch (_phase)
        {
            case Phase.Pending when _active is { } request && request.Elapsed(now) >= _options.RequestTimeoutMs:
                _host.Report("type_ahead_timeout", _buffer.Count);
                Expire(now, "timeout");
                break;
            case Phase.Draining when now >= _deadline:
                _host.Report("type_ahead_drain_timeout", _buffer.Count);
                Advance(now);
                break;
            case Phase.Settling when now >= _deadline:
                RaiseQueued(now);
                break;
        }
    }

    // The hook is going away: returns every held key in order. Queued Ctrl+V presses go back as ordinary keys.
    public IReadOnlyList<KeyEvent> Shutdown()
    {
        if (_active is { } request)
        {
            _active = null;
            _host.ExpireRequest(request.Sequence, "shutdown");
        }
        var keys = _buffer.Select(entry => entry.Key).ToArray();
        _buffer.Clear();
        _queuedTriggers.Clear();
        _phase = Phase.Idle;
        return keys;
    }

    private bool Route(in KeyEvent key, bool isRepeat, long now, IKeySink? capture)
    {
        var virtualKey = key.VirtualKey;

        if (_suppressed.Contains(virtualKey))
        {
            Seen(_suppressed, key, now);
            return true;
        }

        if (_captured.Contains(virtualKey))
        {
            Seen(_captured, key, now);
            if (!key.IsUp && capture is not null)
            {
                var repeat = MakeStroke(key, isRepeat: true);
                if (Accepts(capture, repeat)) _host.DeliverKey(capture, repeat);
            }
            return true;
        }

        // A capture sees key presses first. Modifiers and lock keys are not presses in that sense: holding Shift to
        // type a capital letter must not close the picker.
        if (capture is not null && !key.IsUp && !VirtualKeys.IsModifier(virtualKey) && !VirtualKeys.IsLock(virtualKey))
        {
            var stroke = MakeStroke(key, isRepeat);
            if (IsOfferable(stroke) && Accepts(capture, stroke))
            {
                Swallow(_captured, virtualKey, now);
                _host.DeliverKey(capture, stroke);
                return true;
            }
            _host.DeliverOtherKey(capture);
        }

        return _phase == Phase.Idle ? RouteIdle(key, isRepeat, now) : Hold(key, isRepeat, now);
    }

    private bool RouteIdle(in KeyEvent key, bool isRepeat, long now)
    {
        if (!IsTrigger(key, isRepeat) || !_host.ShouldIntercept()) return false;
        Swallow(_suppressed, key.VirtualKey, now);
        Start(now);
        return true;
    }

    private bool Hold(in KeyEvent key, bool isRepeat, long now)
    {
        var virtualKey = key.VirtualKey;
        if (_queuedTriggers.TryGetValue(virtualKey, out var marker))
        {
            // Repeats and the release of a queued Ctrl+V travel with it until it is raised.
            _buffer.Add(new Entry(key, marker, IsMarker: false));
            if (key.IsUp) _queuedTriggers.Remove(virtualKey);
        }
        else if (IsTrigger(key, isRepeat))
        {
            marker = ++_lastMarker;
            _buffer.Add(new Entry(key, marker, IsMarker: true));
            _queuedTriggers[virtualKey] = marker;
        }
        else
        {
            _buffer.Add(new Entry(key, 0, IsMarker: false));
        }

        if (_phase == Phase.Pending && _buffer.Count > _options.MaxBuffered)
        {
            _host.Report("type_ahead_overflow", _buffer.Count);
            Expire(now, "overflow");
        }
        return true;
    }

    private void Start(long now)
    {
        _active = new Request(++_lastSequence, now, paused: _captureActive);
        _phase = Phase.Pending;
        _host.RaiseRequest(_active.Sequence);
    }

    private void Expire(long now, string reason)
    {
        if (_active is not { } request) return;
        _active = null;
        _host.ExpireRequest(request.Sequence, reason);
        Advance(now);
    }

    // Moves on once nothing is in flight: replays the keys up to the next queued Ctrl+V, waits before raising that
    // one, or goes back to letting keys straight through.
    private void Advance(long now)
    {
        var count = _buffer.FindIndex(entry => entry.IsMarker);
        if (count < 0) count = _buffer.Count;

        if (count > 0)
        {
            var keys = new KeyEvent[count];
            for (var i = 0; i < count; i++) keys[i] = _buffer[i].Key;
            _buffer.RemoveRange(0, count);
            _fence = (byte)(_fence % 127 + 1);
            _phase = Phase.Draining;
            _deadline = now + _options.DrainTimeoutMs;
            _host.Inject(keys, _fence);
        }
        else if (_buffer.Count > 0)
        {
            _phase = Phase.Settling;
            _deadline = now + _options.SettleDelayMs;
        }
        else
        {
            _phase = Phase.Idle;
        }
    }

    // The queued Ctrl+V at the head of the buffer is next. The intercept decision is made now, not when the key was
    // typed: the keys replayed before it may have switched apps, and the block may have expired meanwhile.
    private void RaiseQueued(long now)
    {
        var marker = _buffer[0];
        _buffer.RemoveAt(0);
        var virtualKey = marker.Key.VirtualKey;
        var stillHeld = _queuedTriggers.TryGetValue(virtualKey, out var owner) && owner == marker.MarkerId;
        if (stillHeld) _queuedTriggers.Remove(virtualKey);

        if (_host.ShouldIntercept())
        {
            _buffer.RemoveAll(entry => entry.MarkerId == marker.MarkerId);
            if (stillHeld) Swallow(_suppressed, virtualKey, now);
            Start(now);
            return;
        }

        // Not a smart paste after all: the keys go to the app exactly as typed.
        for (var i = 0; i < _buffer.Count; i++)
            if (_buffer[i].MarkerId == marker.MarkerId) _buffer[i] = _buffer[i] with { MarkerId = 0 };
        _buffer.Insert(0, marker with { MarkerId = 0, IsMarker = false });
        Advance(now);
    }

    // From now on this held key's repeats and its release are swallowed.
    private void Swallow(HashSet<ushort> keys, ushort virtualKey, long now)
    {
        keys.Add(virtualKey);
        _lastSeen[virtualKey] = now;
    }

    // A repeat or the release of a key being swallowed.
    private void Seen(HashSet<ushort> keys, in KeyEvent key, long now)
    {
        if (!key.IsUp)
        {
            _lastSeen[key.VirtualKey] = now;
            return;
        }
        keys.Remove(key.VirtualKey);
        _lastSeen.Remove(key.VirtualKey);
    }

    // A swallowed key that went quiet for longer than any auto-repeat gap was released without the hook seeing it
    // (another hook swallowed the release, or it happened on the secure desktop). Without this, every later press of
    // that key would be swallowed too.
    private void ForgetIfReleaseWasMissed(ushort virtualKey, long now)
    {
        if (!_lastSeen.TryGetValue(virtualKey, out var lastSeen) || now - lastSeen <= MaxRepeatGapMs) return;
        _suppressed.Remove(virtualKey);
        _captured.Remove(virtualKey);
        _lastSeen.Remove(virtualKey);
        // Its release was lost, so this press is a new one, not a repeat.
        _keys.Set(virtualKey, false);
        _host.Report("type_ahead_lost_release", virtualKey);
    }

    // The hook misses events while another desktop has the keyboard (lock screen, UAC prompt), so a key can look
    // held long after it was released. While nothing is held back the system's key state is accurate, so keys the
    // next decision depends on are corrected from it first.
    private void RepairFromSystem(ushort virtualKey)
    {
        foreach (var modifier in ModifierKeys) Repair(modifier);
        Repair(virtualKey);
    }

    private void Repair(ushort virtualKey)
    {
        if (_suppressed.Contains(virtualKey) || _captured.Contains(virtualKey) || _releasedByUs.Contains(virtualKey)) return;
        _keys.Set(virtualKey, _host.IsKeyDownNow(virtualKey));
    }

    // Ctrl+V and nothing else: Ctrl+Shift+V (plain-text paste in many apps) is never touched, and auto-repeat of a
    // held V never starts another request.
    private bool IsTrigger(in KeyEvent key, bool isRepeat) =>
        !key.IsUp && !isRepeat && key.VirtualKey == VirtualKeys.V &&
        _keys.ControlDown && !_keys.ShiftDown && !_keys.AltDown && !_keys.WindowsDown;

    private KeyStroke MakeStroke(in KeyEvent key, bool isRepeat)
    {
        var character = key.VirtualKey == VirtualKeys.Packet ? (char)key.ScanCode : _host.TranslateCharacter(key, _keys);
        if (character is { } c && char.IsControl(c)) character = null;
        return new KeyStroke(key.VirtualKey, character, _keys.Modifiers, isRepeat);
    }

    // Keys pressed with Win, or with Alt when they type nothing (Alt+Tab, Alt+F4), are system shortcuts and always go
    // to the app. AltGr combinations that type a character stay with the capture.
    private static bool IsOfferable(in KeyStroke stroke) =>
        (stroke.Modifiers & KeyModifiers.Windows) == 0 &&
        ((stroke.Modifiers & KeyModifiers.Alt) == 0 || stroke.Character is not null);

    // The sink is outside code running on the hook thread; a failure there must not take the keyboard down.
    private bool Accepts(IKeySink capture, in KeyStroke stroke)
    {
        try
        {
            return capture.Accepts(stroke);
        }
        catch (Exception ex)
        {
            _host.Report("capture_sink_error", 0, ex.GetType().Name);
            return false;
        }
    }

    // MarkerId ties a queued Ctrl+V (IsMarker) to its own repeats and release (same MarkerId, not a marker).
    private readonly record struct Entry(KeyEvent Key, int MarkerId, bool IsMarker);

    // The request being decided, with a clock that stops while a capture has the keyboard.
    private sealed class Request(int sequence, long startedAt, bool paused)
    {
        private long _elapsed;
        private long? _runningSince = paused ? null : startedAt;

        public int Sequence { get; } = sequence;

        public long Elapsed(long now) => _elapsed + (_runningSince is { } since ? now - since : 0);

        public long? Deadline(long timeout) => _runningSince is { } since ? since + timeout - _elapsed : null;

        public void Pause(long now)
        {
            if (_runningSince is not { } since) return;
            _elapsed += now - since;
            _runningSince = null;
        }

        public void Resume(long now) => _runningSince ??= now;
    }
}

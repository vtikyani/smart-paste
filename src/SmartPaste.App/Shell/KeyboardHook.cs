using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// The low-level keyboard hook. It runs on its own thread with its own message loop: the UI thread does clipboard and
// imaging work that can take a while, and a hook sharing that thread would stall every keystroke system-wide (Windows
// silently removes hooks that miss LowLevelHooksTimeout).
//
// The decisions live in InputQueue. This class adapts it to Win32: it installs the hooks, feeds events in, performs
// injections after the hook callback has returned, keeps time, and hands requests and captured keys to the UI thread.
//
// Threads: Start, Stop, Capture, and Dispose may be called from any thread (normally the UI thread). PasteRequested
// and every IKeySink callback except Accepts run on the UI thread. InterceptFilter and IKeySink.Accepts run on the
// hook thread inside the hook callback, so they must be fast and thread-safe.
internal sealed class KeyboardHook : IKeyCapture, IDisposable
{
    // WM_APP + 0x53: commands are waiting in the queue.
    private const uint WakeMessage = 0x8053;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.LowLevelHookProc _keyboardCallback;
    private readonly NativeMethods.LowLevelHookProc _mouseCallback;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly object _lifecycle = new();

    // Hook thread only.
    private readonly Dictionary<int, PasteRequest> _requests = [];
    private readonly List<(KeyEvent[] Keys, byte Fence)> _injections = [];
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _characters = new char[8];
    private InputQueue? _queue;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    private Thread? _thread;
    private uint _threadId;
    private CaptureSession? _capture;
    private Func<IntPtr, bool>? _interceptFilter;

    // The last request number handed out, kept across Stop and Start (hook thread writes, the next hook thread reads).
    private int _lastSequence;

    public KeyboardHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // Kept in fields: the native hooks hold only function pointers, so the delegates must not be collected.
        _keyboardCallback = OnKeyboard;
        _mouseCallback = OnMouse;
    }

    // Asked on the hook thread, with the foreground window, whether a Ctrl+V should become a smart paste. Must return
    // within microseconds.
    public Func<IntPtr, bool>? InterceptFilter
    {
        get => Volatile.Read(ref _interceptFilter);
        set => Volatile.Write(ref _interceptFilter, value);
    }

    // UI thread. Every request must be completed by its handler; keys stay held until then (or the safety timeout).
    public event Action<PasteRequest>? PasteRequested;

    public bool IsRunning
    {
        get
        {
            lock (_lifecycle) return _thread is not null;
        }
    }

    private static long Now => Environment.TickCount64;

    // Installs the hook. False when Windows refused it; the hook stays stopped and Start can be tried again.
    public bool Start()
    {
        lock (_lifecycle)
        {
            if (_thread is not null) return true;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => Run(started))
            {
                IsBackground = true,
                Name = "Smart Paste keyboard hook",
                // Every keystroke on the machine waits for this thread; keep it ahead of ordinary work.
                Priority = ThreadPriority.AboveNormal
            };
            // Tracked before it runs, so Stop can always reach it.
            _thread = thread;
            thread.Start();
            if (started.Task.Wait(StartTimeout) && started.Task.Result) return true;
            // A refused hook ends its thread; a thread that has not answered yet stays tracked so Stop still ends it.
            if (started.Task.IsCompleted) _thread = null;
            Log.Event("hook_start_failed");
            return false;
        }
    }

    // Removes the hooks. Keys still held back are replayed first, so nothing typed is lost.
    public void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            thread = _thread;
            _thread = null;
        }
        if (thread is null) return;
        for (var attempt = 0; attempt < 10 && thread.IsAlive; attempt++)
        {
            var threadId = Volatile.Read(ref _threadId);
            if (threadId != 0 && NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero)) break;
            Thread.Sleep(20);
        }
        if (!thread.Join(StopTimeout)) Log.Event("hook_stop_timeout");
    }

    public IDisposable Capture(IKeySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var session = new CaptureSession(this, sink);
        // Effective at once on the hook thread, which reads the field on every key.
        Volatile.Write(ref _capture, session);
        Post(SyncCapture);
        return session;
    }

    public void Dispose() => Stop();

    private void EndCapture(CaptureSession session)
    {
        if (Interlocked.CompareExchange(ref _capture, null, session) == session) Post(SyncCapture);
    }

    // Hook thread: the mouse hook runs only while a capture is active, and the request clock pauses meanwhile.
    private void SyncCapture()
    {
        var active = Volatile.Read(ref _capture) is not null;
        _queue?.SetCaptureActive(active, Now);
        if (active) InstallMouseHook();
        else RemoveMouseHook();
    }

    private void Post(Action command)
    {
        lock (_lifecycle)
        {
            if (_thread is null) return;
        }
        _commands.Enqueue(command);
        NativeMethods.PostThreadMessage(Volatile.Read(ref _threadId), WakeMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run(TaskCompletionSource<bool> started)
    {
        Volatile.Write(ref _threadId, NativeMethods.GetCurrentThreadId());
        // Creates this thread's message queue before anyone can post to it.
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PmNoRemove);

        // Work and requests left over from the previous hook session (a completion posted just as it stopped) belong
        // to requests that session already resolved.
        while (_commands.TryDequeue(out _)) { }
        _requests.Clear();
        _injections.Clear();

        var queue = new InputQueue(new Host(this), lastSequence: Volatile.Read(ref _lastSequence));
        queue.Seed(IsKeyDownNow);
        _queue = queue;
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl, _keyboardCallback, NativeMethods.GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            Log.Event("hook_install_error", ("status", Marshal.GetLastPInvokeError()));
            _queue = null;
            started.TrySetResult(false);
            return;
        }
        started.TrySetResult(true);

        try
        {
            Pump(queue);
        }
        catch (Exception ex)
        {
            // The hook thread is the last place a failure may linger: report it and release the keyboard below.
            Log.Error("hook_thread_error", ex);
        }
        finally
        {
            Shutdown(queue);
        }
    }

    private void Pump(InputQueue queue)
    {
        while (true)
        {
            NativeMethods.MsgWaitForMultipleObjectsEx(
                0, IntPtr.Zero, WaitMilliseconds(queue), NativeMethods.QsAllInput, NativeMethods.MwmoInputAvailable);

            // Hook callbacks are delivered inside PeekMessage.
            while (NativeMethods.PeekMessage(out var message, IntPtr.Zero, 0, 0, NativeMethods.PmRemove))
            {
                if (message.Message == NativeMethods.WmQuit) return;
                if (message.Window != IntPtr.Zero)
                {
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessage(ref message);
                }
            }

            while (_commands.TryDequeue(out var command)) command();
            queue.Tick(Now);
            InjectPending(queue);
        }
    }

    private static uint WaitMilliseconds(InputQueue queue)
    {
        if (queue.NextDeadline is not { } deadline) return NativeMethods.Infinite;
        var remaining = deadline - Now;
        return remaining <= 0 ? 0 : (uint)Math.Min(remaining, int.MaxValue);
    }

    // Outside the hook callback: SendInput from inside it would compete with the very event being processed.
    private void InjectPending(InputQueue queue)
    {
        while (_injections.Count > 0)
        {
            var (keys, fence) = _injections[0];
            _injections.RemoveAt(0);
            if (!KeySender.Replay(keys, fence)) queue.OnInjectionFailed(fence, Now);
        }
    }

    private void Shutdown(InputQueue queue)
    {
        var held = queue.Shutdown();
        RemoveMouseHook();
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _queue = null;

        // With the hook gone no fence can come back, so everything goes out plainly, decided batches first.
        foreach (var (keys, _) in _injections) KeySender.Replay(keys, fence: null);
        _injections.Clear();
        if (held.Count > 0) KeySender.Replay(held, fence: null);
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _queue is { } queue)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                var key = new KeyEvent(
                    (ushort)data.VkCode,
                    (ushort)data.ScanCode,
                    (data.Flags & NativeMethods.LlkhfUp) != 0,
                    (data.Flags & NativeMethods.LlkhfExtended) != 0);
                var swallow = data.ExtraInfo == KeySender.InjectedMarker
                    ? queue.OnOwnInjected(key, Now)
                    : queue.OnKey(key, Now, Volatile.Read(ref _capture));
                if (swallow) return 1;
            }
            catch (Exception ex)
            {
                // A failure here must never take the keyboard down: report it off-thread and let the key through.
                ReportLater("hook_callback_error", ("error", ex.GetType().Name));
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsButtonDown((int)wParam) && Volatile.Read(ref _capture) is { } session)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);
            var (x, y) = (data.Point.X, data.Point.Y);
            Deliver(session, sink => sink.OnPointerDown(x, y));
        }
        // Mouse input is only observed, never swallowed.
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool IsButtonDown(int message) => message is NativeMethods.WmLButtonDown or NativeMethods.WmRButtonDown
        or NativeMethods.WmMButtonDown or NativeMethods.WmXButtonDown;

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl, _mouseCallback, NativeMethods.GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero) Log.Event("mouse_hook_install_error", ("status", Marshal.GetLastPInvokeError()));
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private static bool IsKeyDownNow(ushort virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // Hook thread, possibly inside the callback: create the request and hand it to the UI thread.
    private void Raise(int sequence)
    {
        Volatile.Write(ref _lastSequence, sequence);
        var window = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var request = new PasteRequest(window, processId, sequence, Stopwatch.GetTimestamp(), CompleteFromAnyThread);
        _requests[sequence] = request;
        _dispatcher.BeginInvoke(() => DeliverRequest(request));
    }

    // UI thread.
    private void DeliverRequest(PasteRequest request)
    {
        var handler = PasteRequested;
        if (handler is null)
        {
            // Nobody decides pastes (the app is shutting down or not wired yet): behave like a normal Ctrl+V.
            KeySender.SendPaste();
            request.Complete();
            return;
        }
        try
        {
            handler(request);
        }
        catch
        {
            // Release the keyboard before the failure goes on to the dispatcher's crash handling.
            request.Complete();
            throw;
        }
    }

    private void CompleteFromAnyThread(PasteRequest request) => Post(() =>
    {
        _requests.Remove(request.Sequence);
        _queue?.Complete(request.Sequence, Now);
    });

    private void Expire(int sequence, string reason)
    {
        if (_requests.Remove(sequence, out var request)) request.Expire();
        ReportLater("paste_request_expired", ("reason", reason));
    }

    // Hook thread: the character a key types in the foreground app's layout, given the modifiers the user holds.
    private char? Translate(in KeyEvent key, KeyStateTracker keys)
    {
        var thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var layout = NativeMethods.GetKeyboardLayout(thread);
        for (var virtualKey = 0; virtualKey < _keyState.Length; virtualKey++)
            _keyState[virtualKey] = keys.IsDown((ushort)virtualKey) ? (byte)0x80 : (byte)0;
        if (keys.ShiftDown) _keyState[VirtualKeys.Shift] = 0x80;
        if (keys.ControlDown) _keyState[VirtualKeys.Control] = 0x80;
        if (keys.AltDown) _keyState[VirtualKeys.Menu] = 0x80;
        if ((NativeMethods.GetKeyState(VirtualKeys.Capital) & 1) != 0) _keyState[VirtualKeys.Capital] |= 0x01;
        if ((NativeMethods.GetKeyState(VirtualKeys.NumLock) & 1) != 0) _keyState[VirtualKeys.NumLock] |= 0x01;
        var count = NativeMethods.ToUnicodeEx(
            key.VirtualKey, key.ScanCode, _keyState, _characters, _characters.Length, NativeMethods.ToUnicodeKeepKeyboardState, layout);
        return count >= 1 ? _characters[0] : null;
    }

    // UI-thread delivery to a capture, dropped when that capture has ended meanwhile.
    private void Deliver(IKeySink capture, Action<IKeySink> action)
    {
        if (capture is not CaptureSession session) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(Volatile.Read(ref _capture), session)) action(session.Sink);
        });
    }

    // The log writes to disk; never from the hook thread, where it would delay every keystroke.
    private static void ReportLater(string eventName, params (string Key, object? Value)[] fields) =>
        ThreadPool.QueueUserWorkItem(_ => Log.Event(eventName, fields));

    private sealed class Host(KeyboardHook hook) : IInputQueueHost
    {
        public bool ShouldIntercept()
        {
            var filter = hook.InterceptFilter;
            if (filter is null) return false;
            try
            {
                return filter(NativeMethods.GetForegroundWindow());
            }
            catch (Exception ex)
            {
                ReportLater("intercept_filter_error", ("error", ex.GetType().Name));
                return false;
            }
        }

        public bool IsKeyDownNow(ushort virtualKey) => KeyboardHook.IsKeyDownNow(virtualKey);

        public char? TranslateCharacter(in KeyEvent key, KeyStateTracker keys) => hook.Translate(key, keys);

        public void RaiseRequest(int sequence) => hook.Raise(sequence);

        public void ExpireRequest(int sequence, string reason) => hook.Expire(sequence, reason);

        public void Inject(IReadOnlyList<KeyEvent> keys, byte fence) => hook._injections.Add((keys.ToArray(), fence));

        public void DeliverKey(IKeySink capture, KeyStroke stroke) => hook.Deliver(capture, sink => sink.OnKey(stroke));

        public void DeliverOtherKey(IKeySink capture) => hook.Deliver(capture, sink => sink.OnOtherKey());

        public void Report(string eventName, int count, string? reason) =>
            ReportLater(eventName, ("count", count), ("reason", reason));
    }

    // The queue sees the session itself as the sink, so a delivery can check it still belongs to the current capture.
    private sealed class CaptureSession(KeyboardHook owner, IKeySink sink) : IKeySink, IDisposable
    {
        public IKeySink Sink { get; } = sink;

        public bool Accepts(in KeyStroke key) => Sink.Accepts(key);

        public void OnKey(KeyStroke key) => Sink.OnKey(key);

        public void OnOtherKey() => Sink.OnOtherKey();

        public void OnPointerDown(int x, int y) => Sink.OnPointerDown(x, y);

        public void Dispose() => owner.EndCapture(this);
    }
}

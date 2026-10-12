namespace SmartPaste.App.Infrastructure;

// One Smart Paste per user session and data folder. Starting it again (from the Start menu, say) wakes the
// running copy, which opens its settings, instead of starting a second keyboard hook. Starting it with --exit asks
// the running copy to exit instead.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _exit;
    private RegisteredWaitHandle? _activateWait;
    private RegisteredWaitHandle? _exitWait;
    private bool _ownsMutex;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, EventWaitHandle exit, bool isPrimary)
    {
        _mutex = mutex;
        _activate = activate;
        _exit = exit;
        IsPrimary = isPrimary;
        _ownsMutex = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstance Acquire() => Acquire($@"Local\SmartPaste.{AppPaths.InstanceId}");

    // name: the kernel objects' base name. Tests pass their own, so they never meet the instance their host holds.
    internal static SingleInstance Acquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var created);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        var exit = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".exit");
        return new SingleInstance(mutex, activate, exit, created);
    }

    // Second copy: ask the running one to show itself. This copy was just started by the user, so it may take the
    // foreground; it passes that right on, or Windows would keep the running copy's window behind the others.
    public void SignalPrimary()
    {
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        _activate.Set();
    }

    // Second copy, started with --exit: asks the running copy to exit the normal way (so it puts the clipboard back
    // and lets go of held keys), then waits until it has. True when it is gone, false when it was still running when
    // the wait ran out.
    public bool RequestExit(TimeSpan timeout)
    {
        _exit.Set();
        try
        {
            if (!_mutex.WaitOne(timeout)) return false;
        }
        catch (AbandonedMutexException)
        {
            // It ended without letting go of the lock (it crashed or was stopped): gone all the same. This copy owns
            // the lock now either way.
        }
        _ownsMutex = true;
        // Answered. A copy started later must not find the request still waiting and exit right away.
        _exit.Reset();
        return true;
    }

    private static class NativeMethods
    {
        public const uint AsfwAny = unchecked((uint)-1);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool AllowSetForegroundWindow(uint processId);
    }

    // First copy: run the callback (on a thread-pool thread) each time another copy starts.
    public void OnActivationRequested(Action callback) => Listen(ref _activateWait, _activate, callback);

    // First copy: run the callback (on a thread-pool thread) when a copy started with --exit asks this one to exit.
    public void OnExitRequested(Action callback) => Listen(ref _exitWait, _exit, callback);

    private static void Listen(ref RegisteredWaitHandle? registration, WaitHandle signal, Action callback)
    {
        registration?.Unregister(null);
        registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _activateWait?.Unregister(null);
        _exitWait?.Unregister(null);
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // released on a different thread than the one that owns it
        }
        _mutex.Dispose();
        _activate.Dispose();
        _exit.Dispose();
    }
}

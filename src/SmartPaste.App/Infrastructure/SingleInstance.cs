namespace SmartPaste.App.Infrastructure;

// One Smart Paste per user session and data folder. Starting it again (from the Start menu, say) wakes the
// running copy, which opens its settings, instead of starting a second keyboard hook.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, bool isPrimary)
    {
        _mutex = mutex;
        _activate = activate;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstance Acquire()
    {
        var name = $@"Local\SmartPaste.{AppPaths.InstanceId}";
        var mutex = new Mutex(initiallyOwned: true, name, out var created);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        return new SingleInstance(mutex, activate, created);
    }

    // Second copy: ask the running one to show itself. This copy was just started by the user, so it may take the
    // foreground; it passes that right on, or Windows would keep the running copy's window behind the others.
    public void SignalPrimary()
    {
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        _activate.Set();
    }

    private static class NativeMethods
    {
        public const uint AsfwAny = unchecked((uint)-1);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool AllowSetForegroundWindow(uint processId);
    }

    // First copy: run the callback (on a thread-pool thread) each time another copy starts.
    public void OnActivationRequested(Action callback)
    {
        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // released on a different thread than the one that owns it
        }
        _mutex.Dispose();
        _activate.Dispose();
    }
}

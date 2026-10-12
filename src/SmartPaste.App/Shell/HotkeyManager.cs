using System.Runtime.InteropServices;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// Global shortcuts (RegisterHotKey), one per name from HotkeyNames. Lives on the UI thread: create it, call it, and
// dispose it there; Pressed and Changed are raised there too.
internal sealed class HotkeyManager : IHotkeyStatus, IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public HotkeyManager()
    {
        _window = new MessageWindow("SmartPaste.Hotkeys", WndProc);
    }

    // The name of the shortcut that was pressed.
    public event Action<string>? Pressed;

    public event EventHandler? Changed;

    // Sets or clears (null or blank) a shortcut. False when it cannot be used: another app owns the combination, or
    // the text is not a valid shortcut (see Hotkey.TryParse for the reason).
    public bool Set(string name, string? hotkey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            if (_registrations.Remove(name, out var removed))
            {
                Unregister(removed);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            return true;
        }

        if (!_registrations.TryGetValue(name, out var registration))
        {
            registration = new Registration(_nextId++);
            _registrations[name] = registration;
        }

        Hotkey? parsed = Hotkey.TryParse(hotkey, out var key, out var error) ? key : null;
        if (registration.Registered && parsed == registration.Key) return true;

        Unregister(registration);
        registration.Key = parsed;
        if (parsed is { } valid)
        {
            registration.Registered = NativeMethods.RegisterHotKey(
                _window.Handle, registration.Id, valid.NativeModifiers | NativeMethods.ModNoRepeat, (uint)valid.VirtualKey);
            if (!registration.Registered)
            {
                var status = Marshal.GetLastPInvokeError();
                Log.Event("hotkey_unavailable", ("setting", name),
                    ("reason", status == NativeMethods.ErrorHotkeyAlreadyRegistered ? "taken" : "refused"), ("status", status));
            }
        }
        else
        {
            Log.Event("hotkey_invalid", ("setting", name), ("reason", error));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return registration.Registered;
    }

    // True when registered, false when it could not be (taken by another app, or invalid), null when not set.
    public bool? IsRegistered(string name) =>
        _registrations.TryGetValue(name, out var registration) ? registration.Registered : null;

    public void Dispose()
    {
        foreach (var registration in _registrations.Values) Unregister(registration);
        _registrations.Clear();
        _window.Dispose();
    }

    private void Unregister(Registration registration)
    {
        if (!registration.Registered) return;
        NativeMethods.UnregisterHotKey(_window.Handle, registration.Id);
        registration.Registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return IntPtr.Zero;
        handled = true;
        var id = wParam.ToInt32();
        foreach (var (name, registration) in _registrations)
        {
            if (registration.Id != id || !registration.Registered) continue;
            Pressed?.Invoke(name);
            break;
        }
        return IntPtr.Zero;
    }

    private sealed class Registration(int id)
    {
        public int Id { get; } = id;

        public Hotkey? Key { get; set; }

        public bool Registered { get; set; }
    }
}

using System.Runtime.InteropServices;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Shell;

// Watches the clipboard (AddClipboardFormatListener on a message-only window) and reports each new copy: Changed
// first, before anything is read, then TextCopied or ImageCopied when the copy may be read at all.
//
// UI thread: create, call, and dispose it there; every event is raised there. Ignore may be called from any thread.
internal sealed class ClipboardWatcher : IDisposable
{
    private const string PngFormat = "PNG";
    private const int IgnoredCapacity = 16;

    private readonly MessageWindow _window;
    private readonly HashSet<IntPtr> _ignoredOwners = [];
    private readonly object _ignoredGate = new();
    private readonly uint[] _ignoredSequences = new uint[IgnoredCapacity];
    private int _ignoredNext;
    private uint _lastSequence;
    private bool _listening;
    private ImagingThread? _imaging;

    public ClipboardWatcher()
    {
        _window = new MessageWindow("SmartPaste.ClipboardWatcher", WndProc);
    }

    // Read copied screenshots (PNG or bitmap) as well as text. Off unless the user turned it on.
    public bool ReadScreenshots { get; set; }

    public bool IsListening => _listening;

    // Apps whose copies are never read, by executable name ("mstsc.exe"): asked with the clipboard open, before any
    // content is read. Null reads every app's copies (private ones and password managers' are always skipped).
    public Func<string, bool>? IsSkippedOwner { get; set; }

    // A new copy from another app, raised before anything else happens (it disarms the previous block).
    public event Action? Changed;

    // Copied text. Not raised for private copies, or for copies made in a skipped app.
    public event Action<string>? TextCopied;

    // A copied screenshot as PNG, at most 2000 px on its longest side. Only while ReadScreenshots is on, and, like
    // text, never for private copies or copies made in a skipped app.
    public event Action<byte[]>? ImageCopied;

    public void Start()
    {
        if (_listening) return;
        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening) Log.Event("clipboard_listen_error", ("status", Marshal.GetLastPInvokeError()));
    }

    public void Stop()
    {
        if (!_listening) return;
        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }

    // A clipboard sequence number produced by our own write. Kept with the last few: by the time a notification is
    // handled the clipboard may have moved on, so two notifications can read the same number.
    public void Ignore(uint sequence)
    {
        lock (_ignoredGate)
        {
            _ignoredSequences[_ignoredNext] = sequence;
            _ignoredNext = (_ignoredNext + 1) % IgnoredCapacity;
        }
    }

    // Copies owned by this window (the paster's) are our own writes and never reported.
    public void IgnoreOwner(IntPtr window)
    {
        if (window != IntPtr.Zero) _ignoredOwners.Add(window);
    }

    public void Dispose()
    {
        Stop();
        _imaging?.Dispose();
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmClipboardUpdate) return IntPtr.Zero;
        handled = true;
        if (_listening) OnClipboardUpdate();
        return IntPtr.Zero;
    }

    private bool IsIgnored(uint sequence)
    {
        if (sequence == 0) return false;
        lock (_ignoredGate) return Array.IndexOf(_ignoredSequences, sequence) >= 0;
    }

    private void OnClipboardUpdate()
    {
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        // Two notifications can describe one change: both read the current number when handled.
        if (sequence != 0 && sequence == _lastSequence) return;
        _lastSequence = sequence;
        if (IsIgnored(sequence)) return;
        var owner = NativeMethods.GetClipboardOwner();
        if (owner != IntPtr.Zero && _ignoredOwners.Contains(owner)) return;

        Changed?.Invoke();
        Read(sequence);
    }

    private void Read(uint sequence)
    {
        if (!ClipboardAccess.TryOpen(_window.Handle))
        {
            Log.Event("copy_open_failed");
            return;
        }

        string? text = null;
        string? ownerName = null;
        (byte[] Bytes, bool IsPng)? image = null;
        try
        {
            // Read with the clipboard open: the owner cannot change until it is closed, so the privacy check below
            // judges the very content that is read (opening can wait for another app, and a new copy may land meanwhile).
            var owner = NativeMethods.GetClipboardOwner();
            if (owner != IntPtr.Zero && _ignoredOwners.Contains(owner)) return;
            uint ownerProcessId = 0;
            if (owner != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(owner, out ownerProcessId);
            ownerName = ProcessInfo.TryGetName(ownerProcessId);
            if (ownerName is not null && IsSkippedOwner?.Invoke(ownerName) == true)
            {
                Log.Event("copy_skipped", ("reason", "excluded_app"));
                return;
            }

            var formats = ClipboardAccess.EnumerateFormats();
            var names = formats.Where(format => format.Name is not null).Select(format => format.Name!).ToList();
            if (ClipboardPrivacy.ShouldSkip(names, name => ReadDword(formats, name), ownerName))
            {
                Log.Event("copy_skipped", ("reason", "private"));
                return;
            }
            if (formats.Exists(format => format.Id == ClipboardFormatIds.UnicodeText)) text = ClipboardAccess.ReadUnicodeText();
            else if (ReadScreenshots) image = ReadImage(formats);
        }
        finally
        {
            // Held only for the reads above; the conversion happens after it is closed.
            NativeMethods.CloseClipboard();
        }

        if (text is not null)
        {
            if (!string.IsNullOrWhiteSpace(text)) TextCopied?.Invoke(text);
            return;
        }
        if (image is { } found) _ = ConvertAndRaiseAsync(found.Bytes, found.IsPng, sequence);
    }

    private static uint? ReadDword(List<(uint Id, string? Name)> formats, string name)
    {
        foreach (var (id, formatName) in formats)
            if (string.Equals(formatName, name, StringComparison.OrdinalIgnoreCase)) return ClipboardAccess.ReadDword(id);
        return null;
    }

    // PNG keeps transparency and is already compressed, so it wins; then the bitmap formats Windows offers for any
    // copied image.
    private static (byte[] Bytes, bool IsPng)? ReadImage(List<(uint Id, string? Name)> formats)
    {
        foreach (var (id, name) in formats)
            if (string.Equals(name, PngFormat, StringComparison.OrdinalIgnoreCase) && ClipboardAccess.ReadBytes(id) is { } png)
                return (png, true);
        foreach (var format in new[] { ClipboardFormatIds.DibV5, ClipboardFormatIds.Dib })
            if (formats.Exists(f => f.Id == format) && ClipboardAccess.ReadBytes(format) is { } dib)
                return (dib, false);
        return null;
    }

    private async Task ConvertAndRaiseAsync(byte[] bytes, bool isPng, uint sequence)
    {
        try
        {
            _imaging ??= new ImagingThread();
            var png = await _imaging.RunAsync(() => isPng ? ClipboardImage.NormalizePng(bytes) : ClipboardImage.DibToPng(bytes));
            if (png is null)
            {
                Log.Event("copy_image_unreadable", ("kind", isPng ? "png" : "dib"));
                return;
            }
            // A newer copy wins, and so does turning screenshot reading off meanwhile.
            if (sequence != _lastSequence || !ReadScreenshots || !_listening) return;
            ImageCopied?.Invoke(png);
        }
        catch (Exception ex)
        {
            // Fire-and-forget from the message handler: record the failure type, never the content.
            Log.Error("copy_image_error", ex);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Shell;

// Pastes a value as a real paste, so the app's own undo works, without losing what the user copied:
//   1. snapshot the clipboard;
//   2. put the value on it with delayed rendering, so Windows tells us the moment an app reads it, marked so
//      clipboard history and cloud sync skip it;
//   3. send Ctrl+V;
//   4. once the target has read it (plus a short grace period), or after a timeout, put the snapshot back, unless
//      something new was copied meanwhile.
//
// UI thread: create, call, and dispose it there. The value is rendered from this window's messages, so the UI thread
// must keep pumping while a paste is in flight (await it, never block on it).
internal sealed class ClipboardPaster : IDisposable
{
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly MessageWindow _window;
    private readonly ClipboardWatcher _watcher;
    // One paste at a time: the next one must snapshot the user's clipboard, never our borrowed value. Not disposed,
    // because a paste still finishing during shutdown releases it afterwards.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly uint _excludeFormat;
    private readonly uint _historyFormat;
    private readonly uint _cloudFormat;

    private PasteSession? _session;
    private IReadOnlyList<ClipboardItem>? _snapshot;

    // The value our delayed CF_UNICODETEXT stands for, while it is on the clipboard.
    private string? _renderable;

    // Set while we empty a clipboard we own: the WM_DESTROYCLIPBOARD that causes is not someone else taking over.
    private bool _emptyingOwnClipboard;

    public ClipboardPaster(ClipboardWatcher watcher)
    {
        _watcher = watcher;
        _window = new MessageWindow("SmartPaste.ClipboardPaster", WndProc);
        _watcher.IgnoreOwner(_window.Handle);
        _excludeFormat = NativeMethods.RegisterClipboardFormat(ClipboardPrivacy.ExcludeFormat);
        _historyFormat = NativeMethods.RegisterClipboardFormat(ClipboardPrivacy.HistoryFormat);
        _cloudFormat = NativeMethods.RegisterClipboardFormat(ClipboardPrivacy.CloudFormat);
    }

    private enum InstallResult
    {
        Installed,
        // Another app held the clipboard; nothing changed.
        Busy,
        // The user's own text could not be copied; nothing changed.
        SnapshotFailed,
        // The clipboard could not be emptied; nothing changed.
        EmptyFailed,
        // Emptied, but the value could not be fully put on; the snapshot must go back.
        Broken
    }

    public IntPtr WindowHandle => _window.Handle;

    // Cancellation is honored until Ctrl+V is sent; after that the paste is seen through, because restoring the
    // clipboard early could make the target paste the whole original copy.
    public async Task<PasteOutcome> PasteValueAsync(string value, PasteTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(target);
        await _oneAtATime.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!IsForeground(target))
            {
                Log.Event("paste_target_changed");
                return PasteOutcome.TargetChanged;
            }

            var clock = Stopwatch.StartNew();
            var session = new PasteSession(value, target);
            var install = Install(session, out var snapshot);
            if (install != InstallResult.Installed) return await FallBackAsync(install, session, snapshot, target);

            _session = session;
            _snapshot = snapshot;
            if (!KeySender.SendPaste())
            {
                await RestoreWithRetryAsync(session, snapshot);
                Log.Event("paste_clipboard", ("outcome", PasteOutcome.Failed), ("reason", "send_input"));
                return PasteOutcome.Failed;
            }

            try
            {
                await session.Settled.WaitAsync(ConsumeTimeout);
            }
            catch (TimeoutException)
            {
                // The target never read the value; restore anyway so the clipboard is not left borrowed.
            }
            if (session.Consumed && !session.Superseded) await Task.Delay(Grace);

            var restore = await RestoreWithRetryAsync(session, snapshot);
            var outcome = session.Outcome(restore);
            Log.Event("paste_clipboard", ("outcome", outcome), ("source", session.ConsumedBy?.ToString() ?? "none"),
                ("count", session.OtherReaders), ("ms", clock.ElapsedMilliseconds), ("stage", restore));
            return outcome;
        }
        finally
        {
            _session = null;
            _snapshot = null;
            _oneAtATime.Release();
        }
    }

    // A plain Ctrl+V of whatever is on the clipboard, if the target still has the foreground.
    public bool PastePlain(PasteTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsForeground(target))
        {
            Log.Event("paste_target_changed");
            return false;
        }
        return KeySender.SendPaste();
    }

    public void Dispose()
    {
        // Exiting mid-paste: put the user's clipboard back rather than leave the borrowed value behind.
        if (_session is { } session && _snapshot is { } snapshot) Restore(session, snapshot);
        _window.Dispose();
    }

    // The target counts as unchanged when the foreground window is the same, or belongs to the same app (an owned
    // dialog or popup of the target).
    private static bool IsForeground(PasteTarget target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == target.Window) return true;
        if (foreground == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId != 0 && (processId == target.WindowProcessId || processId == target.FocusProcessId);
    }

    private InstallResult Install(PasteSession session, out IReadOnlyList<ClipboardItem> snapshot)
    {
        snapshot = [];
        if (!ClipboardAccess.TryOpen(_window.Handle)) return InstallResult.Busy;
        var emptied = false;
        try
        {
            var items = new List<ClipboardItem>();
            foreach (var (format, name) in ClipboardAccess.EnumerateFormats())
            {
                if (!ClipboardSnapshot.ShouldSnapshot(format, name)) continue;
                if (ClipboardAccess.ReadBytes(format) is { } data)
                {
                    items.Add(new ClipboardItem(format, name, data));
                    continue;
                }
                // The user's text is the one thing a paste must never lose.
                if (format == ClipboardFormatIds.UnicodeText) return InstallResult.SnapshotFailed;
                Log.Event("paste_format_skipped", ("format", (long)format));
            }
            snapshot = items;

            if (!EmptyOwnClipboard()) return InstallResult.EmptyFailed;
            emptied = true;
            _renderable = session.Value;
            var installed = ClipboardAccess.SetDelayed(ClipboardFormatIds.UnicodeText);
            installed &= ClipboardAccess.SetBytes(_excludeFormat, new byte[4]);
            installed &= ClipboardAccess.SetBytes(_historyFormat, new byte[4]);
            installed &= ClipboardAccess.SetBytes(_cloudFormat, new byte[4]);
            return installed ? InstallResult.Installed : InstallResult.Broken;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            // Only our own write is ignored; a failed attempt must not hide a copy the user just made.
            if (emptied) _watcher.Ignore(NativeMethods.GetClipboardSequenceNumber());
        }
    }

    private async Task<PasteOutcome> FallBackAsync(
        InstallResult install, PasteSession session, IReadOnlyList<ClipboardItem> snapshot, PasteTarget target)
    {
        if (install == InstallResult.Broken) await RestoreWithRetryAsync(session, snapshot);
        var pasted = PastePlain(target);
        Log.Event("paste_clipboard", ("outcome", PasteOutcome.Failed), ("reason", install), ("stage", pasted ? "plain" : "none"));
        return PasteOutcome.Failed;
    }

    private async Task<RestoreResult> RestoreWithRetryAsync(PasteSession session, IReadOnlyList<ClipboardItem> snapshot)
    {
        var result = Restore(session, snapshot);
        if (result != RestoreResult.Failed) return result;
        await Task.Delay(RetryDelay);
        result = Restore(session, snapshot);
        if (result == RestoreResult.Failed) Log.Event("paste_restore_failed");
        return result;
    }

    private RestoreResult Restore(PasteSession session, IReadOnlyList<ClipboardItem> snapshot)
    {
        if (session.Superseded) return RestoreResult.LeftAlone;
        if (!ClipboardAccess.TryOpen(_window.Handle)) return RestoreResult.Failed;
        var wrote = false;
        try
        {
            // Checked with the clipboard open, so no other app can write in between.
            if (!session.ShouldRestore(NativeMethods.GetClipboardOwner() == _window.Handle))
            {
                _renderable = null;
                return RestoreResult.LeftAlone;
            }
            if (!EmptyOwnClipboard()) return RestoreResult.Failed;
            wrote = true;
            _renderable = null;
            foreach (var item in ClipboardSnapshot.PlanRestore(snapshot, _historyFormat, _cloudFormat))
                if (!ClipboardAccess.SetBytes(item.Format, item.Data)) Log.Event("paste_restore_format_error", ("format", (long)item.Format));
            return RestoreResult.Restored;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            if (wrote) _watcher.Ignore(NativeMethods.GetClipboardSequenceNumber());
        }
    }

    private bool EmptyOwnClipboard()
    {
        _emptyingOwnClipboard = true;
        try
        {
            return NativeMethods.EmptyClipboard();
        }
        finally
        {
            _emptyingOwnClipboard = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmRenderFormat:
                handled = true;
                if ((uint)wParam.ToInt64() == ClipboardFormatIds.UnicodeText) Render();
                break;
            case NativeMethods.WmRenderAllFormats:
                handled = true;
                RenderBeforeWindowCloses();
                break;
            case NativeMethods.WmDestroyClipboard:
                handled = true;
                if (!_emptyingOwnClipboard)
                {
                    _renderable = null;
                    _session?.OnSuperseded();
                }
                break;
        }
        return IntPtr.Zero;
    }

    // Another app is inside GetClipboardData for our delayed text and holds the clipboard open: only SetClipboardData
    // is allowed here, and it must be quick.
    private void Render()
    {
        if (_renderable is not { } value) return;
        var reader = NativeMethods.GetOpenClipboardWindow();
        uint readerProcessId = 0;
        if (reader != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(reader, out readerProcessId);
        _session?.OnRendered(readerProcessId);
        if (!ClipboardAccess.SetData(ClipboardFormatIds.UnicodeText, ClipboardAccess.AllocateUnicode(value)))
            Log.Event("paste_render_error", ("status", Marshal.GetLastPInvokeError()));
    }

    // Our window is going away while its delayed value is still on the clipboard: render it so readers still get it.
    private void RenderBeforeWindowCloses()
    {
        if (_renderable is not { } value || !NativeMethods.OpenClipboard(_window.Handle)) return;
        try
        {
            if (NativeMethods.GetClipboardOwner() == _window.Handle)
                ClipboardAccess.SetData(ClipboardFormatIds.UnicodeText, ClipboardAccess.AllocateUnicode(value));
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }
}

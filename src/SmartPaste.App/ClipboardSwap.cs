using System.Text;
using SmartPaste.App.Native;
using SmartPaste.Core;

namespace SmartPaste.App;
internal sealed class ClipboardSwap
{
    // Spec value; tune on Windows if Chrome pastes the old clipboard.
    internal static readonly TimeSpan RestoreDelay=TimeSpan.FromMilliseconds(150);
    private static readonly HashSet<uint> HandleFormats=new() { Win32.CfBitmap,Win32.CfEnhMetafile,Win32.CfMetafilePict,Win32.CfPalette,Win32.CfOwnerDisplay,Win32.CfDspBitmap,Win32.CfDspEnhMetafile,Win32.CfDspMetafilePict };
    private readonly ClipboardWatcher _watcher;
    internal ClipboardSwap(ClipboardWatcher watcher) { _watcher=watcher; }
    internal async Task PasteValueAsync(string value)
    {
        var snapshot=new List<(uint format,byte[] data)>();
        if(!Win32.OpenClipboardRetry(_watcher.Handle)) { KeySender.SendPaste(); return; }
        bool swapped=false, snapshotComplete=true, modified=false;
        try
        {
            for(uint format=0;(format=Win32.EnumClipboardFormats(format))!=0;)
            {
                if(HandleFormats.Contains(format)) continue;
                var bytes=Win32.CopyGlobal(Win32.GetClipboardData(format));
                if(bytes is null) { snapshotComplete=false; break; }
                snapshot.Add((format,bytes));
            }
            if(snapshotComplete)
            {
                if(Win32.EmptyClipboard())
                {
                    modified=true;
                    var text=Encoding.Unicode.GetBytes(value+'\0');
                    swapped=Win32.SetGlobal(Win32.CfUnicodeText,text);
                    uint exclude=Win32.RegisterClipboardFormat(ClipboardPrivacy.ExcludeFormat);
                    uint history=Win32.RegisterClipboardFormat(ClipboardPrivacy.HistoryFormat);
                    uint cloud=Win32.RegisterClipboardFormat(ClipboardPrivacy.CloudFormat);
                    swapped &= Win32.SetGlobal(exclude,new byte[4]) && Win32.SetGlobal(history,new byte[4]) && Win32.SetGlobal(cloud,new byte[4]);
                }
            }
        }
        catch { Log.Event("clipboard_swap_error"); swapped=false; snapshotComplete=false; }
        finally { Win32.CloseClipboard(); }
        if(!snapshotComplete)
        {
            if(modified) await Restore(snapshot);
            KeySender.SendPaste(); return;
        }
        if(!swapped) { await Restore(snapshot); KeySender.SendPaste(); return; }
        uint swappedSequence=Win32.GetClipboardSequenceNumber();
        _watcher.Ignore(swappedSequence);
        if(!KeySender.SendPaste()) Log.Event("send_input_error");
        await Task.Delay(RestoreDelay);
        // Something else wrote the clipboard during the delay (usually the user copying again): theirs wins, no
        // restore. Tradeoff: a target app that rewrites the clipboard on paste also skips the restore (logged).
        if(Win32.GetClipboardSequenceNumber()!=swappedSequence) { Log.Event("clipboard_restore_skipped_newer_copy"); return; }
        if(!await Restore(snapshot))
        {
            await Task.Delay(200);
            if(Win32.GetClipboardSequenceNumber()!=swappedSequence) { Log.Event("clipboard_restore_skipped_newer_copy"); return; }
            if(!await Restore(snapshot)) Log.Event("clipboard_restore_failed");
        }
    }
    private Task<bool> Restore(List<(uint format,byte[] data)> snapshot)
    {
        try
        {
            if(!Win32.OpenClipboardRetry(_watcher.Handle)) { Log.Event("clipboard_restore_error"); return Task.FromResult(false); }
            try
            {
                if(!Win32.EmptyClipboard()) { Log.Event("clipboard_restore_error"); return Task.FromResult(false); }
                foreach(var item in snapshot) if(!Win32.SetGlobal(item.format,item.data)) Log.Event("clipboard_restore_format_error",("format",item.format));
            }
            finally { Win32.CloseClipboard(); }
            _watcher.Ignore(Win32.GetClipboardSequenceNumber());
            return Task.FromResult(true);
        }
        catch { Log.Event("clipboard_restore_error"); return Task.FromResult(false); }
    }
}

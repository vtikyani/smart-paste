using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SmartPaste.App.Native;
using SmartPaste.Core;

namespace SmartPaste.App;
internal sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    private readonly AppSettings _settings;
    // Sequence numbers of our own writes. Kept (not removed on first match) because WM_CLIPBOARDUPDATE
    // reads the current number when handled, so two posted updates can both see the same one.
    private readonly Queue<uint> _ignored=new();
    private readonly object _gate=new();
    private bool _listening;
    internal event Action? ClipboardChanged;
    internal event Action<string>? TextCopied;
    internal event Action<byte[]>? ImageCopied;
    internal ClipboardWatcher(AppSettings settings)
    {
        _settings=settings;
        CreateHandle(new CreateParams { Parent=Win32.HwndMessage,Caption="SmartPaste Clipboard" });
    }
    internal void Start() { if(!_listening) _listening=Win32.AddClipboardFormatListener(Handle); }
    internal void Stop() { if(_listening) { Win32.RemoveClipboardFormatListener(Handle); _listening=false; } }
    internal void Ignore(uint sequence) { lock(_gate) { _ignored.Enqueue(sequence); while(_ignored.Count>16) _ignored.Dequeue(); } }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==Win32.WmClipboardUpdate) try { OnUpdate(); } catch { Log.Event("copy_error"); }
        base.WndProc(ref m);
    }
    private void OnUpdate()
    {
        uint sequence=Win32.GetClipboardSequenceNumber();
        lock(_gate) if(_ignored.Contains(sequence)) return;
        ClipboardChanged?.Invoke();
        if(!Win32.OpenClipboardRetry(Handle)) return;
        string? text=null; byte[]? image=null; uint imageFormat=0;
        try
        {
            var names=new List<string>(); var formats=new List<uint>(); var named=new Dictionary<string,uint>(StringComparer.OrdinalIgnoreCase);
            for(uint f=0;(f=Win32.EnumClipboardFormats(f))!=0;)
            {
                formats.Add(f);
                var buffer=new StringBuilder(256);
                if(Win32.GetClipboardFormatName(f,buffer,buffer.Capacity)>0) { string name=buffer.ToString(); names.Add(name); named[name]=f; }
            }
            uint? ReadDword(string name)
            {
                if(!named.TryGetValue(name,out uint f)) return null;
                var handle=Win32.GetClipboardData(f);
                if(handle==IntPtr.Zero || Win32.GlobalSize(handle).ToUInt64()<4) return null;
                var ptr=Win32.GlobalLock(handle); if(ptr==IntPtr.Zero) return null;
                try { return unchecked((uint)Marshal.ReadInt32(ptr)); } finally { Win32.GlobalUnlock(handle); }
            }
            var owner=Win32.GetClipboardOwner(); Win32.GetWindowThreadProcessId(owner,out uint pid);
            if(ClipboardPrivacy.ShouldSkip(names,ReadDword,Win32.ProcessName(pid))) { Log.Event("copy_skipped_private"); return; }
            if(formats.Contains(Win32.CfUnicodeText))
            {
                var handle=Win32.GetClipboardData(Win32.CfUnicodeText);
                var bytes=Win32.CopyGlobal(handle);
                if(bytes is not null) text=Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            else if(_settings.ParseScreenshots)
            {
                if(named.TryGetValue("PNG",out imageFormat)) image=Win32.CopyGlobal(Win32.GetClipboardData(imageFormat));
                else if(formats.Contains(Win32.CfDibV5)) { imageFormat=Win32.CfDibV5; image=Win32.CopyGlobal(Win32.GetClipboardData(imageFormat)); }
                else if(formats.Contains(Win32.CfDib)) { imageFormat=Win32.CfDib; image=Win32.CopyGlobal(Win32.GetClipboardData(imageFormat)); }
            }
        }
        finally { Win32.CloseClipboard(); }
        if(text is not null) TextCopied?.Invoke(text);
        else if(image is not null)
        {
            try { var png=NormalizeImage(image,imageFormat); if(png is not null) ImageCopied?.Invoke(png); }
            catch { Log.Event("image_decode_error"); }
        }
    }
    private static byte[]? NormalizeImage(byte[] bytes,uint format)
    {
        using var stream=new MemoryStream();
        if(format==Win32.CfDib || format==Win32.CfDibV5)
        {
            if(bytes.Length<40) return null;
            int header=BitConverter.ToInt32(bytes,0), bpp=BitConverter.ToInt16(bytes,14), colors=BitConverter.ToInt32(bytes,32);
            if(header<40 || header>bytes.Length) return null;
            int compression=BitConverter.ToInt32(bytes,16);
            // BI_BITFIELDS (3) with a 40-byte header is followed by three DWORD color masks before the pixels.
            int masks=header==40 && compression==3?12:0;
            int offset=14+header+masks+(colors>0?colors*(bpp<=8?4:0):bpp<=8?(1<<bpp)*4:0);
            using var writer=new BinaryWriter(stream,Encoding.ASCII,true);
            writer.Write((ushort)0x4D42); writer.Write(bytes.Length+14); writer.Write(0); writer.Write(offset);
            writer.Write(bytes); stream.Position=0;
        }
        else { stream.Write(bytes); stream.Position=0; }
        using var source=new Bitmap(stream);
        double scale=Math.Min(1.0,1568.0/Math.Max(source.Width,source.Height));
        using var resized=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
        using(var graphics=Graphics.FromImage(resized)) graphics.DrawImage(source,0,0,resized.Width,resized.Height);
        using var output=new MemoryStream(); resized.Save(output,ImageFormat.Png); return output.ToArray();
    }
    public void Dispose() { Stop(); DestroyHandle(); }
}

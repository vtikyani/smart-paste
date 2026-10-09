using System.Drawing.Imaging;
using PaddleOcrNet.Models;
using PaddleOcrNet.Services;
using SmartPaste.App.Native;

namespace SmartPaste.App;
internal sealed class LabelOcr : IAsyncDisposable
{
    private PaddleOcrService? _service;
    private readonly SemaphoreSlim _gate=new(1,1);
    private Task? _warmup;
    internal void WarmUpAsync() => _warmup ??= Task.Run(async () =>
    {
        bool entered=false;
        try
        {
            await _gate.WaitAsync(); entered=true;
            _service ??= new PaddleOcrService();
            using var image=new Bitmap(16,16);
            using(var graphics=Graphics.FromImage(image)) graphics.Clear(Color.White);
            using var stream=new MemoryStream(); image.Save(stream,ImageFormat.Png);
            await _service.ExtractTextFromImage(stream.ToArray(),OcrLanguage.English);
        }
        catch { Log.Event("ocr_warmup_error"); }
        finally { if(entered) _gate.Release(); }
    });
    internal async Task<string?> ReadAsync(IntPtr hwnd,Rectangle? bounds,CancellationToken ct)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(1200);
        try
        {
            var field=Anchor(hwnd,bounds);
            var virtualScreen=SystemInformation.VirtualScreen;
            int right=field.Left+40, bottom=field.Bottom;
            var crop=Rectangle.FromLTRB(right-600,bottom-120,right,bottom);
            crop=Rectangle.Intersect(crop,virtualScreen); if(crop.Width<=0 || crop.Height<=0) return null;
            using var bitmap=new Bitmap(crop.Width,crop.Height);
            using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(crop.Location,Point.Empty,crop.Size);
            using var stream=new MemoryStream(); bitmap.Save(stream,ImageFormat.Png);
            await _gate.WaitAsync(budget.Token);
            try
            {
                _service ??=new PaddleOcrService();
                var result=await _service.ExtractTextFromImage(stream.ToArray(),OcrLanguage.English).WaitAsync(budget.Token);
                var lines=result.Lines.Select(line=>line.Text).Where(line=>!string.IsNullOrWhiteSpace(line)).ToArray();
                if(lines.Length==0) return null;
                return lines[^1].Length<3 && lines.Length>1?lines[^2]+" "+lines[^1]:lines[^1];
            }
            finally { _gate.Release(); }
        }
        catch(OperationCanceledException) { return null; }
        catch { Log.Event("ocr_error"); return null; }
    }
    private static Rectangle Anchor(IntPtr hwnd,Rectangle? bounds)
    {
        uint thread=Win32.GetWindowThreadProcessId(hwnd,out _);
        var info=new Win32.GuiThreadInfo { Size=System.Runtime.InteropServices.Marshal.SizeOf<Win32.GuiThreadInfo>() };
        if(Win32.GetGUIThreadInfo(thread,ref info) && info.HwndCaret!=IntPtr.Zero)
        {
            var point=new Win32.Point(info.CaretRect.Left,info.CaretRect.Bottom);
            if(Win32.ClientToScreen(info.HwndCaret,ref point)) return new Rectangle(point.X,point.Y,1,1);
        }
        if(bounds.HasValue && bounds.Value.Width>0) return bounds.Value;
        Win32.GetCursorPos(out var cursor); return new Rectangle(cursor.X,cursor.Y,1,1);
    }
    public async ValueTask DisposeAsync()
    {
        // ConfigureAwait(false): Exit waits on this from the UI thread.
        if(!await _gate.WaitAsync(2000).ConfigureAwait(false)) return;
        try { if(_service is not null) await _service.DisposeAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}

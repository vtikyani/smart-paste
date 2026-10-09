using System.Diagnostics;
using SmartPaste.App.Native;
using SmartPaste.Core;

namespace SmartPaste.App;
// Every path that does not paste a value ends in a plain paste: Ctrl+V is never lost.
internal sealed class PasteController
{
    private static readonly TimeSpan Budget=TimeSpan.FromMilliseconds(2500); // picker time is exempt
    private readonly ProfileStore _store;
    private readonly FocusInspector _focus;
    private readonly LabelOcr _ocr;
    private readonly ClipboardSwap _swap;
    private readonly HttpClient _http;
    private readonly LabelCache _cache=new(Path.Combine(AppSettings.DirectoryPath,"label-cache.json"));
    private int _busy;
    internal PasteController(ProfileStore store,FocusInspector focus,LabelOcr ocr,ClipboardSwap swap,HttpClient http)
    { _store=store; _focus=focus; _ocr=ocr; _swap=swap; _http=http; }
    internal async void SmartPasteRequested(IntPtr hwnd)
    {
        if(Interlocked.Exchange(ref _busy,1)!=0) { PlainPaste(hwnd); return; }
        var clock=Stopwatch.StartNew(); string source="none",key="none",reason="fallback";
        bool pasted=false;
        try
        {
            using var deadline=new CancellationTokenSource(Budget);
            var profile=_store.Current;
            if(profile is null) { reason="no profile"; return; }
            var field=await _focus.InspectAsync(TimeSpan.FromMilliseconds(400));
            if(field is null) { reason="no focus"; return; }
            if(field.IsPassword) { reason="password"; return; }
            if(!field.HasUsableLabel)
            {
                var label=await _ocr.ReadAsync(hwnd,_focus.LastBounds,deadline.Token);
                if(!string.IsNullOrWhiteSpace(label)) field=field with { OcrText=label };
            }
            if(deadline.IsCancellationRequested) { reason="timeout"; return; }
            var jev=new JevClient(_http,CredentialStore.Get(CredentialStore.TypeSafe),CredentialStore.Get(CredentialStore.OpenRouter));
            var decision=await new FieldDecider(_cache,jev).DecideAsync(field,profile,deadline.Token);
            source=decision.Source; key=decision.Key??"none"; reason=decision.Reason;
            if(deadline.IsCancellationRequested) { reason="timeout"; return; }
            if(decision.Kind==DecisionKind.Value && decision.Value is not null)
            { await _swap.PasteValueAsync(decision.Value); pasted=true; }
            else if(decision.Kind==DecisionKind.Picker)
            {
                using var picker=new PickerForm(decision.Candidates,hwnd);
                picker.ShowDialog();
                if(!await RefocusAsync(hwnd)) { reason="refocus failed"; pasted=true; return; } // never paste into the wrong window
                if(picker.Chosen is { } chosen) { key=chosen.Key; await _swap.PasteValueAsync(chosen.Value); pasted=true; }
                else reason="picker canceled";
            }
        }
        catch { reason="error"; Log.Event("paste_error"); }
        finally
        {
            if(!pasted) PlainPaste(hwnd);
            Log.Event("paste",("source",source),("key",key),("reason",reason),("ms",clock.ElapsedMilliseconds));
            Interlocked.Exchange(ref _busy,0);
        }
    }
    private static void PlainPaste(IntPtr hwnd)
    {
        if(Win32.GetForegroundWindow()!=hwnd) Win32.SetForegroundWindow(hwnd);
        if(!KeySender.SendPaste()) Log.Event("send_input_error");
    }
    // After the picker closes, give focus back to the target before injecting Ctrl+V into it.
    private static async Task<bool> RefocusAsync(IntPtr hwnd)
    {
        Win32.SetForegroundWindow(hwnd);
        for(int i=0;i<20 && Win32.GetForegroundWindow()!=hwnd;i++) await Task.Delay(15);
        return Win32.GetForegroundWindow()==hwnd;
    }
}

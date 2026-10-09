using SmartPaste.Core;

namespace SmartPaste.App;
internal sealed class CopyPipeline : IDisposable
{
    private readonly ClipboardWatcher _watcher;
    private readonly ProfileStore _store;
    private readonly HttpClient _http;
    private CancellationToken _parseToken;
    internal CopyPipeline(ClipboardWatcher watcher,ProfileStore store,HttpClient http)
    {
        _watcher=watcher; _store=store; _http=http;
        watcher.ClipboardChanged+=OnChanged;
        watcher.TextCopied+=OnText;
        watcher.ImageCopied+=OnImage;
    }
    private void OnChanged() => _parseToken=_store.ResetParse();
    private void OnText(string text)
    {
        try
        {
            if(!TextHeuristics.LooksMultiField(text)) return;
            var fast=TextHeuristics.Extract(text);
            var profile=ProfileParser.FromFastFields(fast,DateTimeOffset.UtcNow);
            if(profile.IsMultiField) _store.Set(profile,_parseToken);
            string? key=CredentialStore.Get(CredentialStore.OpenRouter);
            if(!string.IsNullOrWhiteSpace(key)) _=ParseText(text,fast,key,_parseToken);
        }
        catch { Log.Event("parse_fast_error"); }
    }
    private async Task ParseText(string text,FastFields fast,string key,CancellationToken token)
    {
        try { var llm=await new OpenRouterParser(_http,key).ParseTextAsync(text,DateTimeOffset.UtcNow,token);
            var merged=ProfileParser.Merge(llm,fast); if(merged.IsMultiField) _store.Set(merged,token); }
        catch(OperationCanceledException) { }
        catch { Log.Event("parse_text_error"); }
    }
    private void OnImage(byte[] png)
    {
        try { string? key=CredentialStore.Get(CredentialStore.OpenRouter);
            if(!string.IsNullOrWhiteSpace(key)) _=ParseImage(png,key,_parseToken); }
        catch { Log.Event("parse_image_error"); }
    }
    private async Task ParseImage(byte[] png,string key,CancellationToken token)
    {
        try { var profile=await new OpenRouterParser(_http,key).ParseImageAsync(png,DateTimeOffset.UtcNow,token);
            if(profile.IsMultiField) _store.Set(profile,token); }
        catch(OperationCanceledException) { }
        catch { Log.Event("parse_image_error"); }
    }
    public void Dispose() { _watcher.ClipboardChanged-=OnChanged; _watcher.TextCopied-=OnText; _watcher.ImageCopied-=OnImage; }
}

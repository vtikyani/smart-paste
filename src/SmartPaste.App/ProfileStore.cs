using SmartPaste.Core;

namespace SmartPaste.App;
internal sealed class ProfileStore : IDisposable
{
    private readonly object _gate=new();
    private Profile? _current;
    private CancellationTokenSource _parse=new();
    internal event EventHandler? Changed;
    internal Profile? Current { get { lock(_gate) return _current?.IsFresh(DateTimeOffset.UtcNow)==true?_current:null; } }
    internal CancellationToken ResetParse()
    {
        lock(_gate) { _parse.Cancel(); _parse.Dispose(); _parse=new(); _current=null; }
        Changed?.Invoke(this,EventArgs.Empty);
        return _parse.Token;
    }
    internal void Clear() => ResetParse();
    internal void Set(Profile profile,CancellationToken token)
    {
        lock(_gate) { if(token.IsCancellationRequested || !profile.IsMultiField) return; _current=profile; }
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public void Dispose() { lock(_gate) { _parse.Cancel(); _parse.Dispose(); } }
}

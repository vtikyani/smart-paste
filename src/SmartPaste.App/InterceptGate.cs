using SmartPaste.App.Native;

namespace SmartPaste.App;
internal sealed class InterceptGate
{
    internal static readonly HashSet<string> Blocked=new(StringComparer.OrdinalIgnoreCase) { "mstsc.exe","vmconnect.exe","msrdc.exe","1Password.exe" };
    private readonly AppSettings _settings;
    private readonly ProfileStore _store;
    private readonly Dictionary<uint,(DateTime until,bool allowed)> _cache=new();
    private readonly bool _selfElevated;
    internal InterceptGate(AppSettings settings,ProfileStore store)
    {
        _settings=settings; _store=store;
        using var process=System.Diagnostics.Process.GetCurrentProcess();
        _selfElevated=IsElevated(process.Handle)==true;
    }
    internal bool ShouldIntercept(IntPtr hwnd)
    {
        if(!_settings.Enabled || _store.Current?.IsMultiField!=true || hwnd==IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(hwnd,out uint pid);
        if(pid==0 || pid==(uint)Environment.ProcessId) return false;
        if(_cache.TryGetValue(pid,out var hit) && hit.until>DateTime.UtcNow) return hit.allowed;
        IntPtr process=Win32.OpenProcess(Win32.ProcessQueryLimitedInformation,false,pid);
        if(process==IntPtr.Zero) return false;
        bool allowed;
        try { allowed=Win32.ProcessName(pid) is { } name && !Blocked.Contains(name) && (_selfElevated || IsElevated(process)==false); }
        finally { Win32.CloseHandle(process); }
        _cache[pid]=(DateTime.UtcNow.AddSeconds(5),allowed);
        if(_cache.Count>128) _cache.Clear();
        return allowed;
    }
    private static bool? IsElevated(IntPtr process)
    {
        if(!Win32.OpenProcessToken(process,Win32.TokenQuery,out var token)) return null;
        try { return Win32.GetTokenInformation(token,Win32.TokenElevation,out int elevation,sizeof(int),out _) ? elevation!=0 : null; }
        finally { Win32.CloseHandle(token); }
    }
}

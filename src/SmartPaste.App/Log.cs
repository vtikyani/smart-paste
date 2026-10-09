using System.Text;

namespace SmartPaste.App;
internal static class Log
{
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SmartPaste","logs");
    private static readonly object Gate=new();
    private static readonly HashSet<string> Sources=new(StringComparer.Ordinal) { "none","cache","rules","jev" };
    private static readonly HashSet<string> Reasons=new(StringComparer.Ordinal) {
        "fallback","matched","entity matched","key absent","no match","jev error","low confidence",
        "none selected","multiple values","password","no label","picker canceled","no focus","no profile","error",
        "sensitive label","timeout","refocus failed" };
    // Values and clipboard text are never logged. Callers pass only field labels, key names, counts, and durations.
    internal static void Event(string name, params (string k, object v)[] fields)
    {
        if(!SafeToken(name)) return;
        lock(Gate) try
        {
            Directory.CreateDirectory(Folder);
            foreach(var file in Directory.EnumerateFiles(Folder,"smartpaste-*.log"))
                if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-7)) File.Delete(file);
            var line=new StringBuilder(DateTimeOffset.Now.ToString("O")).Append(' ').Append(name);
            foreach(var (k,v) in fields)
            {
                bool safe=k switch
                {
                    "source" => v is string s && Sources.Contains(s),
                    "reason" => v is string s && Reasons.Contains(s),
                    "key" => v is string s && SafeToken(s),
                    "ms" or "format" => v is int or uint or long,
                    _ => false
                };
                if(safe) line.Append(' ').Append(k).Append('=').Append(v);
            }
            File.AppendAllText(Path.Combine(Folder,$"smartpaste-{DateTime.Now:yyyyMMdd}.log"),line.AppendLine().ToString());
        }
        catch { }
    }
    private static bool SafeToken(string value) => value.Length is >0 and <=64 &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}

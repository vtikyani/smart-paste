using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste.App.Infrastructure;

// Privacy-safe diagnostics. The log can only ever hold event names, allow-listed field names, numbers, and
// short tokens (no spaces, no '@', no free text). Clipboard text, profile values, field labels, and exception
// messages are never written: messages from .NET can quote the input that failed to parse.
internal static partial class Log
{
    private const int RetentionDays = 14;
    private static readonly object Gate = new();
    private static string _directory = AppPaths.LogDirectory;

    // Fields that may carry a number.
    private static readonly HashSet<string> NumberFields = new(StringComparer.Ordinal)
    {
        "ms", "count", "status", "format", "attempt", "length", "items", "percent"
    };

    // Fields that may carry a short token such as a key name, a reason code, or a process name.
    private static readonly HashSet<string> TokenFields = new(StringComparer.Ordinal)
    {
        "source", "reason", "key", "route", "outcome", "stage", "app", "error", "kind", "setting", "mode",
        "from", "to", "version", "control"
    };

    public static string Directory => _directory;

    // Points the log at a folder and deletes log files older than two weeks. Safe to call more than once.
    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            _directory = directory;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.log"))
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // Records one event, for example Log.Event("paste", ("source", "jev"), ("ms", 84)).
    // Fields that are not allow-listed, or whose value is not a number or a safe token, are dropped.
    public static void Event(string name, params (string Key, object? Value)[] fields)
    {
        if (!EventName().IsMatch(name)) return;
        var line = new StringBuilder(96)
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(' ').Append(name);
        foreach (var (key, value) in fields)
            if (Format(key, value) is { } text) line.Append(' ').Append(key).Append('=').Append(text);
        Write($"smartpaste-{DateTime.Now:yyyyMMdd}.log", line.AppendLine().ToString());
    }

    // Records a handled failure: the event name plus the exception type, never its message.
    public static void Error(string name, Exception exception, params (string Key, object? Value)[] fields) =>
        Event(name, [.. fields, ("error", exception.GetType().Name)]);

    // Writes a crash report: exception types and stack traces (method names and line numbers), never messages.
    public static void Crash(Exception exception, string origin)
    {
        var report = new StringBuilder()
            .Append("Smart Paste crash report").AppendLine()
            .Append("time: ").Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)).AppendLine()
            .Append("origin: ").Append(Token().IsMatch(origin) ? origin : "unknown").AppendLine()
            .Append("version: ").Append(AppInfo.Version).AppendLine()
            .Append("os: ").Append(Environment.OSVersion.VersionString).AppendLine();
        var depth = 0;
        for (var current = exception; current is not null && depth < 8; current = current.InnerException, depth++)
        {
            report.AppendLine().Append(depth == 0 ? "exception: " : "inner: ").Append(current.GetType().FullName).AppendLine();
            if (current.StackTrace is { } stack) report.Append(stack).AppendLine();
        }
        Write($"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log", report.ToString());
        Event("crash", ("stage", origin), ("error", exception.GetType().Name));
    }

    private static string? Format(string key, object? value)
    {
        if (NumberFields.Contains(key))
            return value switch
            {
                int or long or uint or ulong or short or ushort or byte => Convert.ToString(value, CultureInfo.InvariantCulture),
                double d when double.IsFinite(d) => d.ToString("0.###", CultureInfo.InvariantCulture),
                float f when float.IsFinite(f) => f.ToString("0.###", CultureInfo.InvariantCulture),
                TimeSpan t => ((long)t.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                _ => null
            };
        if (TokenFields.Contains(key))
            return value switch
            {
                string s when Token().IsMatch(s) => s,
                Enum e => e.ToString(),
                bool b => b ? "true" : "false",
                _ => null
            };
        return null;
    }

    private static void Write(string fileName, string text)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, fileName), text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Smart Paste log write failed: {ex.GetType().Name}");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex EventName();

    [GeneratedRegex("^[A-Za-z0-9_.\\-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}

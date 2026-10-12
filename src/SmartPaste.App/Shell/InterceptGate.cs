using System.Collections.Concurrent;
using SmartPaste.Core;

namespace SmartPaste.App.Shell;

internal readonly record struct ProcessFacts(string? Name, int? IntegrityLevel);

// What the gate needs to know about windows and processes. Injectable, so the rules are tested without real windows.
internal interface IWindowFacts
{
    uint CurrentProcessId { get; }

    int CurrentIntegrityLevel { get; }

    long NowMs { get; }

    string? GetClassName(IntPtr window);

    // The process that receives keyboard input for the window.
    uint GetInputProcessId(IntPtr window);

    ProcessFacts GetProcessFacts(uint processId);
}

// Decides whether Ctrl+V in the foreground window may become a smart paste. Called on the keyboard hook thread for
// every Ctrl+V, so it is fast (process facts are cached for a few seconds) and thread-safe.
internal sealed class InterceptGate
{
    private const long CacheMs = 5000;
    private const int CacheLimit = 256;

    // Remote sessions and virtual machines forward Ctrl+V to another computer with its own clipboard.
    private static readonly string[] RemoteApps =
    [
        "mstsc.exe", // Remote Desktop Connection
        "msrdc.exe", // the session window of the Remote Desktop app and Windows App
        "msrdcw.exe", // the Remote Desktop app
        "Windows365.exe", // Windows App
        "vmconnect.exe" // Hyper-V
    ];

    // Consoles and terminals have their own idea of paste (and of Ctrl+V).
    private static readonly HashSet<string> TerminalClasses = new(StringComparer.Ordinal)
    {
        "ConsoleWindowClass",
        "CASCADIA_HOSTING_WINDOW_CLASS",
        "PseudoConsoleWindow",
        "mintty"
    };

    private static readonly HashSet<string> Blocked = new(
        RemoteApps.Concat(ClipboardPrivacy.BlockedOwnerProcesses), StringComparer.OrdinalIgnoreCase);

    private readonly IWindowFacts _facts;
    private readonly ConcurrentDictionary<uint, (long Expires, ProcessFacts Facts)> _cache = new();
    private HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    public InterceptGate()
        : this(new WindowFacts())
    {
    }

    internal InterceptGate(IWindowFacts facts)
    {
        _facts = facts;
    }

    // Apps where Ctrl+V is never intercepted, whatever the user's settings say. For the settings page.
    public static IReadOnlyList<string> BuiltInBlockedApps { get; } =
        Blocked.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyCollection<string> TerminalWindowClasses => TerminalClasses;

    public bool ShouldIntercept(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        if (_facts.GetClassName(window) is { } className && TerminalClasses.Contains(className)) return false;

        var processId = _facts.GetInputProcessId(window);
        if (processId == 0 || processId == _facts.CurrentProcessId) return false;

        var facts = Lookup(processId);
        // A process we cannot even name is one we stay out of the way of.
        if (facts.Name is not { } name) return false;
        if (Blocked.Contains(name) || Volatile.Read(ref _excluded).Contains(name)) return false;

        // Windows blocks input and clipboard messages from a lower integrity level (UIPI), so a paste into an
        // elevated app would silently do nothing. An unknown level counts as higher.
        return facts.IntegrityLevel is { } level && level <= _facts.CurrentIntegrityLevel;
    }

    // Executable names as the settings hold them ("slack.exe"); a missing ".exe" is added.
    public void UpdateExcludedApps(IEnumerable<string> apps)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var name = app?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            excluded.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
        }
        Volatile.Write(ref _excluded, excluded);
    }

    private ProcessFacts Lookup(uint processId)
    {
        var now = _facts.NowMs;
        if (_cache.TryGetValue(processId, out var cached) && cached.Expires > now) return cached.Facts;
        var facts = _facts.GetProcessFacts(processId);
        if (_cache.Count >= CacheLimit) _cache.Clear();
        _cache[processId] = (now + CacheMs, facts);
        return facts;
    }
}

// The real window and process facts.
internal sealed class WindowFacts : IWindowFacts
{
    public uint CurrentProcessId { get; } = (uint)Environment.ProcessId;

    public int CurrentIntegrityLevel { get; } = ProcessInfo.CurrentIntegrityLevel();

    public long NowMs => Environment.TickCount64;

    public string? GetClassName(IntPtr window)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    // A UWP app's top-level window is a frame owned by ApplicationFrameHost.exe; the app itself owns the core window
    // inside it, which is what receives input.
    public uint GetInputProcessId(IntPtr window)
    {
        if (GetClassName(window) == "ApplicationFrameWindow")
        {
            var core = NativeMethods.FindWindowEx(window, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (core != IntPtr.Zero) window = core;
        }
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    public ProcessFacts GetProcessFacts(uint processId) =>
        new(ProcessInfo.TryGetName(processId), ProcessInfo.TryGetIntegrityLevel(processId));
}

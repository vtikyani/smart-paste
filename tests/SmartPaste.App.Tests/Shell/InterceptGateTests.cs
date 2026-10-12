using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

public sealed class InterceptGateTests
{
    private const int Medium = ProcessInfo.MediumIntegrity;
    private const int High = ProcessInfo.HighIntegrity;
    private static readonly IntPtr Window = new(0x100);

    private static (InterceptGate Gate, FakeFacts Facts) Gate(string? processName, int? level = Medium, string className = "Chrome_WidgetWin_1")
    {
        var facts = new FakeFacts { ClassName = className, ProcessId = 42, Process = new ProcessFacts(processName, level) };
        return (new InterceptGate(facts), facts);
    }

    [Fact]
    public void AnOrdinaryAppIsIntercepted()
    {
        Assert.True(Gate("chrome.exe").Gate.ShouldIntercept(Window));
    }

    [Fact]
    public void NoWindowNoProcessOrOurOwnProcessIsNot()
    {
        var (gate, facts) = Gate("chrome.exe");
        Assert.False(gate.ShouldIntercept(IntPtr.Zero));
        facts.ProcessId = 0;
        Assert.False(gate.ShouldIntercept(Window));
        facts.ProcessId = facts.CurrentProcessId;
        Assert.False(gate.ShouldIntercept(Window));
    }

    [Fact]
    public void AnElevatedAppIsSkippedWhenWeAreNot()
    {
        Assert.False(Gate("regedit.exe", High).Gate.ShouldIntercept(Window));
    }

    [Fact]
    public void AnElevatedAppIsInterceptedWhenWeAreElevatedToo()
    {
        var (gate, facts) = Gate("regedit.exe", High);
        facts.CurrentIntegrityLevel = High;
        Assert.True(gate.ShouldIntercept(Window));
    }

    [Fact]
    public void LowerIntegrityAppsAreIntercepted()
    {
        // UWP apps run in an AppContainer at low integrity.
        Assert.True(Gate("WhatsApp.exe", ProcessInfo.LowIntegrity).Gate.ShouldIntercept(Window));
    }

    [Fact]
    public void AnUnknownIntegrityLevelOrNameIsSkipped()
    {
        Assert.False(Gate("protected.exe", level: null).Gate.ShouldIntercept(Window));
        Assert.False(Gate(processName: null).Gate.ShouldIntercept(Window));
    }

    [Theory]
    [InlineData("mstsc.exe")]
    [InlineData("MSRDC.EXE")]
    [InlineData("msrdcw.exe")]
    [InlineData("Windows365.exe")]
    [InlineData("vmconnect.exe")]
    [InlineData("1Password.exe")]
    public void RemoteSessionsVirtualMachinesAndPasswordManagersAreSkipped(string app)
    {
        Assert.False(Gate(app).Gate.ShouldIntercept(Window));
    }

    [Theory]
    [InlineData("ConsoleWindowClass")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("PseudoConsoleWindow")]
    [InlineData("mintty")]
    public void ConsolesAndTerminalsAreSkipped(string className)
    {
        Assert.False(Gate("WindowsTerminal.exe", className: className).Gate.ShouldIntercept(Window));
    }

    [Fact]
    public void TheUsersExcludedAppsAreSkipped()
    {
        var (gate, _) = Gate("Slack.exe");
        gate.UpdateExcludedApps(["slack", " notepad.exe ", ""]);
        Assert.False(gate.ShouldIntercept(Window));

        gate.UpdateExcludedApps([]);
        Assert.True(gate.ShouldIntercept(Window));
    }

    [Fact]
    public void ProcessFactsAreCachedForAFewSeconds()
    {
        var (gate, facts) = Gate("chrome.exe");
        gate.ShouldIntercept(Window);
        gate.ShouldIntercept(Window);
        Assert.Equal(1, facts.Lookups);

        facts.NowMs += 4999;
        gate.ShouldIntercept(Window);
        Assert.Equal(1, facts.Lookups);

        // After the cache runs out a changed process (say, now elevated) is seen.
        facts.NowMs += 1;
        facts.Process = new ProcessFacts("chrome.exe", High);
        Assert.False(gate.ShouldIntercept(Window));
        Assert.Equal(2, facts.Lookups);
    }

    [Fact]
    public void ExclusionsApplyAtOnceDespiteTheCache()
    {
        var (gate, _) = Gate("code.exe");
        Assert.True(gate.ShouldIntercept(Window));
        gate.UpdateExcludedApps(["Code.exe"]);
        Assert.False(gate.ShouldIntercept(Window));
    }

    [Fact]
    public void TheBuiltInListIsSortedAndIncludesCorePasswordManagers()
    {
        var apps = InterceptGate.BuiltInBlockedApps;
        Assert.Contains("mstsc.exe", apps);
        Assert.Contains("vmconnect.exe", apps);
        Assert.Contains("1Password.exe", apps);
        Assert.Equal(apps.Order(StringComparer.OrdinalIgnoreCase), apps);
    }

    private sealed class FakeFacts : IWindowFacts
    {
        public uint CurrentProcessId { get; set; } = 7;

        public int CurrentIntegrityLevel { get; set; } = Medium;

        public long NowMs { get; set; } = 10_000;

        public string? ClassName { get; set; }

        public uint ProcessId { get; set; }

        public ProcessFacts Process { get; set; }

        public int Lookups { get; private set; }

        public string? GetClassName(IntPtr window) => ClassName;

        public uint GetInputProcessId(IntPtr window) => ProcessId;

        public ProcessFacts GetProcessFacts(uint processId)
        {
            Lookups++;
            return Process;
        }
    }
}

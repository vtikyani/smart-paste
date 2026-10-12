using SmartPaste.App.Infrastructure;

// The log is the one place app data could leak to disk, so these tests pin down what it refuses to write.
public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-log-" + Guid.NewGuid().ToString("N"));

    public LogTests() => Log.Initialize(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Written() =>
        string.Concat(Directory.EnumerateFiles(_dir, "smartpaste-*.log").Select(File.ReadAllText));

    [Fact]
    public void WritesAllowListedNumbersAndTokens()
    {
        Log.Event("paste", ("source", "jev"), ("key", "first_name"), ("ms", 84), ("app", "chrome.exe"));
        var text = Written();
        Assert.Contains("paste source=jev key=first_name ms=84 app=chrome.exe", text);
    }

    [Theory]
    [InlineData("key", "jordan@example.com")]
    [InlineData("reason", "Jordan Rivera")]
    [InlineData("value", "07081")]
    [InlineData("label", "First name")]
    public void DropsAnythingThatCouldBeUserText(string field, string value)
    {
        Log.Event("probe", (field, value));
        var text = Written();
        Assert.Contains("probe", text);
        Assert.DoesNotContain(value, text);
    }

    [Fact]
    public void RejectsBadEventNames()
    {
        Log.Event("Jordan Rivera");
        Assert.False(Directory.EnumerateFiles(_dir, "smartpaste-*.log").Any());
    }

    [Fact]
    public void ErrorsKeepTheTypeButNeverTheMessage()
    {
        Log.Error("parse_error", new FormatException("String 'jordan@example.com' was not recognized"));
        var text = Written();
        Assert.Contains("error=FormatException", text);
        Assert.DoesNotContain("jordan", text);
    }

    [Fact]
    public void CrashReportHasStackButNoMessage()
    {
        try { throw new InvalidOperationException("secret value 201-555-0100"); }
        catch (InvalidOperationException ex) { Log.Crash(ex, "test_origin"); }
        var crash = File.ReadAllText(Directory.EnumerateFiles(_dir, "crash-*.log").Single());
        Assert.Contains("System.InvalidOperationException", crash);
        Assert.Contains(nameof(CrashReportHasStackButNoMessage), crash);
        Assert.DoesNotContain("201-555-0100", crash);
    }
}

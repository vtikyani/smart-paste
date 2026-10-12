using System.Globalization;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;
using SmartPaste.App.UI.Tray;

// The words the tray and flyout use for each state, and the small text helpers behind them.
public sealed class StatusTextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // 2:15 PM today, in local time.
    private static readonly DateTimeOffset Now = new(DateTime.Today.AddHours(14).AddMinutes(15));

    private static AppState State(Action<AppState>? change = null)
    {
        var state = new AppState { IsEnabled = true, JevStatus = ServiceStatus.Ready, ParsingStatus = ServiceStatus.Ready };
        change?.Invoke(state);
        return state;
    }

    private static ArmedProfile Armed(TimeSpan left) =>
        new(DocSample.Profile(Now), Now.AddMinutes(-2), Now + left, ProfileOrigin.CopiedText, ParsedByAi: false);

    [Fact]
    public void TooltipSaysReadyWhenWaiting() =>
        Assert.Equal("Smart Paste: ready", StatusText.Tooltip(State(), Now, English));

    [Fact]
    public void TooltipCountsArmedFieldsAndMinutesLeft() =>
        Assert.Equal("Smart Paste: 10 fields armed, 12 min left",
            StatusText.Tooltip(State(s => s.Armed = Armed(TimeSpan.FromMinutes(11.2))), Now, English));

    [Fact]
    public void TooltipSaysWhenAPauseEnds() =>
        Assert.Equal("Smart Paste: paused until " + Time(Now.AddMinutes(85)),
            StatusText.Tooltip(State(s => s.PausedUntil = Now.AddMinutes(85)), Now, English));

    // "3:40 PM", spelled the way this machine's culture data spells it (some insert a narrow space before PM).
    private static string Time(DateTimeOffset at) => at.ToString("t", English);

    [Fact]
    public void TooltipSaysOffWhenTurnedOff() =>
        Assert.Equal("Smart Paste: off", StatusText.Tooltip(State(s => s.IsEnabled = false), Now, English));

    [Fact]
    public void TooltipSaysReadingWhileParsing() =>
        Assert.Equal("Smart Paste: reading your copy…", StatusText.Tooltip(State(s => s.IsParsing = true), Now, English));

    [Fact]
    public void TooltipNamesTheProblemAndStillCountsTheArmedBlock()
    {
        var state = State(s =>
        {
            s.JevStatus = ServiceStatus.KeyRejected;
            s.Armed = Armed(TimeSpan.FromMinutes(5));
        });
        Assert.Equal("Smart Paste: Jev key rejected; 10 fields armed, 5 min left", StatusText.Tooltip(state, Now, English));
    }

    [Fact]
    public void TooltipFitsTheShellsLimit()
    {
        var state = State(s =>
        {
            s.ParsingStatus = ServiceStatus.OutOfCredits;
            s.Armed = Armed(TimeSpan.FromMinutes(59));
        });
        Assert.True(StatusText.Tooltip(state, Now, English).Length < 128);
    }

    [Fact]
    public void FlyoutStatusIsAFewWords()
    {
        Assert.Equal("Ready for your next copy", StatusText.FlyoutStatus(State(), Now, English));
        Assert.Equal("Armed", StatusText.FlyoutStatus(State(s => s.Armed = Armed(TimeSpan.FromMinutes(3))), Now, English));
        Assert.Equal("Reading your copy…", StatusText.FlyoutStatus(State(s => s.IsParsing = true), Now, English));
        Assert.Equal("Off", StatusText.FlyoutStatus(State(s => s.IsEnabled = false), Now, English));
        Assert.Equal("Paused until " + Time(Now.AddMinutes(30)),
            StatusText.FlyoutStatus(State(s => s.PausedUntil = Now.AddMinutes(30)), Now, English));
    }

    [Theory]
    [InlineData(1, "1 field")]
    [InlineData(2, "2 fields")]
    [InlineData(17, "17 fields")]
    public void FieldCountIsSingularForOne(int count, string expected) => Assert.Equal(expected, StatusText.FieldCount(count));

    [Theory]
    [InlineData(900, "15 min left")]
    [InlineData(661, "12 min left")]
    [InlineData(60, "1 min left")]
    [InlineData(59, "under a minute left")]
    [InlineData(0, "under a minute left")]
    public void TimeLeftRoundsUp(int seconds, string expected) => Assert.Equal(expected, StatusText.TimeLeft(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ClearsInMatchesTimeLeft()
    {
        Assert.Equal("Clears in 12 min", StatusText.ClearsIn(TimeSpan.FromSeconds(661)));
        Assert.Equal("Clears in under a minute", StatusText.ClearsIn(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void UntilNamesTheDayWhenItIsNotToday()
    {
        Assert.Equal(Time(Now.AddMinutes(105)), StatusText.Until(Now.AddMinutes(105), Now, English));
        Assert.Equal("tomorrow", StatusText.Until(new DateTimeOffset(DateTime.Today.AddDays(1)), Now, English));
        var tomorrowMorning = new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(9));
        Assert.Equal("tomorrow at " + Time(tomorrowMorning), StatusText.Until(tomorrowMorning, Now, English));
        var later = new DateTimeOffset(DateTime.Today.AddDays(3).AddHours(9));
        Assert.Equal(later.ToString("ddd ", English) + Time(later), StatusText.Until(later, Now, English));
    }

    [Fact]
    public void ServicesPutRejectedKeysFirst()
    {
        Assert.Equal("Jev key rejected", StatusText.Services(ServiceStatus.KeyRejected, ServiceStatus.OutOfCredits).Text);
        Assert.Equal("OpenRouter key rejected", StatusText.Services(ServiceStatus.OutOfCredits, ServiceStatus.KeyRejected).Text);
        Assert.Equal("Jev is out of credits", StatusText.Services(ServiceStatus.OutOfCredits, ServiceStatus.Ready).Text);
    }

    [Theory]
    [InlineData(ServiceStatus.Ready, "Jev ready", "Good")]
    [InlineData(ServiceStatus.NotConfigured, "Add a key in Settings", "Neutral")]
    [InlineData(ServiceStatus.Unreachable, "Offline · using built-in labels", "Neutral")]
    [InlineData(ServiceStatus.RateLimited, "Jev is busy · using built-in labels", "Caution")]
    [InlineData(ServiceStatus.KeyRejected, "Jev key rejected", "Critical")]
    [InlineData(ServiceStatus.OutOfCredits, "Jev is out of credits", "Caution")]
    public void ServicesDescribeJev(ServiceStatus jev, string text, string tone)
    {
        var summary = StatusText.Services(jev, ServiceStatus.Ready);
        Assert.Equal(text, summary.Text);
        Assert.Equal(Enum.Parse<Tone>(tone), summary.Tone);
        // Only a problem the user must fix gets an explanation (and the flyout's banner).
        Assert.Equal(jev is ServiceStatus.KeyRejected or ServiceStatus.OutOfCredits, summary.IsProblem);
    }

    [Fact]
    public void ArmedValuesAreMaskedUntilRevealed()
    {
        var shortValue = new FieldRowViewModel("First name", "Jordan", reveal: false);
        var longValue = new FieldRowViewModel("Email", "jordan@example.com", reveal: false);
        // Every hidden value looks the same, so not even its length shows.
        Assert.Equal("••••••••", shortValue.DisplayValue);
        Assert.Equal(shortValue.DisplayValue, longValue.DisplayValue);
        // Screen readers hear "hidden", never the dots or the value.
        Assert.Equal("First name: hidden", shortValue.SpokenText);

        shortValue.SetReveal(true);
        Assert.Equal("Jordan", shortValue.DisplayValue);
        Assert.Equal("First name: Jordan", shortValue.SpokenText);
        shortValue.SetReveal(false);
        Assert.Equal(DisplayText.Masked, shortValue.DisplayValue);
    }

    [Fact]
    public void ArmedBlockGroupsFieldsByEntityAndHidesValuesByDefault()
    {
        var block = new ArmedBlockViewModel(Armed(TimeSpan.FromMinutes(12)), reveal: false, Now);
        Assert.Equal(["Person", "Address"], block.Groups.Select(g => g.Title));
        Assert.Equal(["First name", "Last name", "Full name", "Email", "Phone"], block.Groups[0].Rows.Select(r => r.Name));
        Assert.All(block.Groups.SelectMany(g => g.Rows), row => Assert.Equal(DisplayText.Masked, row.DisplayValue));
        Assert.Equal("10 fields", block.CountText);
        Assert.Equal("Clears in 12 min", block.ClearsInText);
        Assert.InRange(block.TimeLeft, 0.85, 0.87);
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(50, "1 min ago")]
    [InlineData(150, "3 min ago")]
    [InlineData(7200, "2 h ago")]
    [InlineData(200_000, "2 d ago")]
    public void AgoIsShortAndRounded(int seconds, string expected) => Assert.Equal(expected, DisplayText.Ago(Now.AddSeconds(-seconds), Now));

    [Fact]
    public void ShortcutsSplitIntoKeyCaps()
    {
        Assert.Equal(["Win", "Alt", "V"], DisplayText.Keys("Win+Alt+V"));
        Assert.Equal(["Ctrl", "Shift", "Space"], DisplayText.Keys(" Ctrl + Shift + Space "));
        Assert.Empty(DisplayText.Keys(null));
        Assert.Empty(DisplayText.Keys("  "));
    }

    [Fact]
    public void OneLineFlattensAndTrims()
    {
        Assert.Equal("123 Main St Apt 4B", DisplayText.OneLine("123 Main St\r\nApt 4B", 40));
        Assert.Equal("Springf…", DisplayText.OneLine("Springfield", 8));
    }

    [Fact]
    public void LabelsAndAppNamesReadNaturally()
    {
        Assert.Equal("Shipping address", DisplayText.Sentence(" shipping address "));
        Assert.Equal("chrome", DisplayText.AppName("chrome.exe"));
        Assert.Equal("Code", DisplayText.AppName("Code"));
    }
}

using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings;
using SmartPaste.App.UI.Settings.Pages;
using Wpf.Ui.Controls;

namespace SmartPaste.App.Tests.Settings;

// Small pure rules behind the settings pages: wording of times and counts, app-name cleanup, service alerts,
// and how Jev requests are routed.
public sealed class TextAndRulesTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 12, 15, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 12, 15, 0, 0)));

    [Fact]
    public void PauseTimesReadNaturally()
    {
        Assert.StartsWith("Paused until ", StatusText.PausedUntil(Afternoon.AddMinutes(40), Afternoon));
        Assert.DoesNotContain("tomorrow", StatusText.Moment(Afternoon.AddMinutes(40), Afternoon));
        Assert.StartsWith("tomorrow at ", StatusText.Moment(Afternoon.AddDays(1), Afternoon));
        Assert.Contains(" at ", StatusText.Moment(Afternoon.AddDays(3), Afternoon));
        Assert.DoesNotContain("tomorrow", StatusText.Moment(Afternoon.AddDays(3), Afternoon));
    }

    [Theory]
    [InlineData(5, "5 minutes")]
    [InlineData(15, "15 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(120, "2 hours")]
    public void MinutesReadNaturally(int minutes, string expected) => Assert.Equal(expected, StatusText.Minutes(minutes));

    [Fact]
    public void CountsUseSingularAndPlural()
    {
        Assert.Equal("1 label", StatusText.Count(1, "label", "labels"));
        Assert.Equal("0 labels", StatusText.Count(0, "label", "labels"));
        Assert.Equal("12 labels", StatusText.Count(12, "label", "labels"));
    }

    [Theory]
    [InlineData("Slack", "Slack.exe")]
    [InlineData("slack.exe", "slack.exe")]
    [InlineData("  code.EXE ", "code.EXE")]
    [InlineData("\"C:\\Program Files\\Slack\\slack.exe\"", "slack.exe")]
    [InlineData("C:/Tools/notepad++.exe", "notepad++.exe")]
    public void ExcludedAppNamesAreCleanedUp(string typed, string expected)
    {
        Assert.True(ExcludedAppNames.TryNormalize(typed, [], ["1password.exe"], out var exe, out var error));
        Assert.Equal(expected, exe);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("", "Type the app's program name, like slack.exe.")]
    [InlineData("   ", "Type the app's program name, like slack.exe.")]
    [InlineData(".exe", "Type the app's program name, like slack.exe.")]
    [InlineData("sla*ck", "Program names can't contain characters like * ? : < > or |.")]
    [InlineData("SLACK", "SLACK.exe is already on your list.")]
    [InlineData("1Password", "Smart Paste already leaves 1Password.exe alone.")]
    public void ExcludedAppNamesExplainWhatToFix(string typed, string expected)
    {
        Assert.False(ExcludedAppNames.TryNormalize(typed, ["slack.exe"], ["1password.exe"], out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void OneAlertWhenParsingAndJevShareTheSameProblem()
    {
        var alerts = ServiceAlerts.For(ServiceStatus.KeyRejected, ServiceStatus.KeyRejected, "OpenRouter");
        var alert = Assert.Single(alerts);
        Assert.Equal(InfoBarSeverity.Error, alert.Severity);
        Assert.Equal("OpenRouter didn't accept your key", alert.Title);
        Assert.Contains("copied text is read on this PC and built-in labels pick values", alert.Message);
    }

    [Fact]
    public void SeparateAlertsForSeparateServices()
    {
        var alerts = ServiceAlerts.For(ServiceStatus.Ready, ServiceStatus.OutOfCredits, "TypeSafe");
        var alert = Assert.Single(alerts);
        Assert.Equal(InfoBarSeverity.Warning, alert.Severity);
        Assert.Equal("Your TypeSafe account is out of credits", alert.Title);
        Assert.Contains("built-in labels pick values", alert.Message);

        Assert.Equal(2, ServiceAlerts.For(ServiceStatus.Unreachable, ServiceStatus.KeyRejected, "TypeSafe").Count);
        Assert.Empty(ServiceAlerts.For(ServiceStatus.Ready, ServiceStatus.NotConfigured, "OpenRouter"));
    }

    [Theory]
    [InlineData(Infrastructure.JevRoute.Auto, true, "TypeSafe")]
    [InlineData(Infrastructure.JevRoute.Auto, false, "OpenRouter")]
    [InlineData(Infrastructure.JevRoute.TypeSafe, false, "TypeSafe")]
    [InlineData(Infrastructure.JevRoute.OpenRouter, true, "OpenRouter")]
    public void RouteMapsToAService(Infrastructure.JevRoute route, bool typeSafeSaved, string service) =>
        Assert.Equal(service, JevViewModel.JevService(route, typeSafeSaved));

    [Fact]
    public void RouteSummarySaysWhatHappensNow()
    {
        Assert.Equal("Right now that's TypeSafe.", JevViewModel.DescribeRoute(Infrastructure.JevRoute.Auto, true, true));
        Assert.Equal("Right now that's OpenRouter.", JevViewModel.DescribeRoute(Infrastructure.JevRoute.Auto, false, true));
        Assert.Equal("No key is saved yet, so built-in labels pick values for now.", JevViewModel.DescribeRoute(Infrastructure.JevRoute.Auto, false, false));
        Assert.Contains("No TypeSafe key", JevViewModel.DescribeRoute(Infrastructure.JevRoute.TypeSafe, false, true));
        Assert.Equal("Jev requests go through OpenRouter.", JevViewModel.DescribeRoute(Infrastructure.JevRoute.OpenRouter, true, true));
    }

    [Theory]
    [InlineData("anthropic/claude-haiku-5.5", true)]
    [InlineData("meta-llama/llama-3.1-8b-instruct:free", true)]
    [InlineData("openai/gpt-5", true)]
    [InlineData("claude-haiku", false)]
    [InlineData("anthropic/", false)]
    [InlineData("/model", false)]
    [InlineData("anthropic/claude haiku", false)]
    public void ModelIdsLookLikeVendorSlashModel(string id, bool valid) => Assert.Equal(valid, JevViewModel.IsValidModelId(id));
}

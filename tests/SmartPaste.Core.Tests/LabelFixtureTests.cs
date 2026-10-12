using System.Text.Json;
using SmartPaste.Core;
using Xunit.Abstractions;

// Real form labels run through the decider's offline fallback (no Jev key): learned labels are empty here, so this
// measures the built-in label list and keyword rules, including the sensitive-label and "someone else" checks.
// At runtime Jev decides every paste; these numbers are what the user gets when Jev is unavailable.
public class LabelFixtureTests(ITestOutputHelper output)
{
    private sealed record Row(string Label, string Source, string Expected);

    // One value for every standard key, so the decision's key is the label's meaning.
    private static readonly Profile Everything = new(
        [new Entity("person", FieldKeys.Standard.ToDictionary(key => key, key => $"value of {key}"))],
        new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RealFormLabelsPassAtLeastNinetyPercent()
    {
        var (passed, total, report) = await Run("form-labels.json", "FALLBACK PASS RATE");
        Assert.True(total >= 60);
        Assert.True(passed * 100.0 / total >= 90, report);
    }

    // A regression set, not a blind holdout: these labels were written after the seed list existed, but the seed
    // and rules were tuned on them afterwards. The floor only catches a regression in the offline fallback.
    [Fact]
    public async Task RegressionLabelsKeepTheirFallbackRate()
    {
        var (passed, total, report) = await Run("form-labels-regression.json", "REGRESSION SET PASS RATE");
        Assert.True(passed * 100.0 / total >= 70, report);
    }

    private async Task<(int Passed, int Total, string Report)> Run(string fixture, string title)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var decider = new FieldDecider(new LabelCache(null), jev: null);
        var failures = new List<string>();
        foreach (var row in rows)
        {
            var field = new FieldContext(row.Label, null, null, null, "Edit", false, []);
            var decision = await decider.DecideAsync(field, Everything, CancellationToken.None);
            var got = decision.Key ?? "none";
            if (got != row.Expected) failures.Add($"{row.Source}: {row.Label} expected {row.Expected}, got {got} ({decision.Reason})");
        }
        var passed = rows.Length - failures.Count;
        var report = $"{title}: {passed}/{rows.Length} = {passed * 100.0 / rows.Length:F1}%" +
                     Environment.NewLine + string.Join(Environment.NewLine, failures);
        output.WriteLine(report);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(fixture, ".pass-rate.txt")), report);
        return (passed, rows.Length, report);
    }
}

using System.Text.Json;
using SmartPaste.Core;
using Xunit.Abstractions;

public class LabelFixtureTests(ITestOutputHelper output)
{
    private sealed record Row(string Label, string Source, string Expected);
    [Fact]
    public void RealFormLabelsPassAtLeastNinetyPercent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "form-labels.json");
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(rows.Length >= 60);
        var cache = new LabelCache(null);
        var failures = new List<string>();
        foreach (var row in rows)
        {
            var label = LabelNormalizer.Normalize(row.Label);
            var got = cache.TryGet(label, out var key) ? key : KeywordRules.Match(label) ?? "none";
            if (got != row.Expected) failures.Add($"{row.Source}: {row.Label} expected {row.Expected}, got {got}");
        }
        var passed = rows.Length - failures.Count;
        var pct = passed * 100.0 / rows.Length;
        var report = $"PASS RATE: {passed}/{rows.Length} = {pct:F1}%";
        output.WriteLine(report);
        foreach (var failure in failures) output.WriteLine(failure);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fixture-pass-rate.txt"), report + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(pct >= 90, report + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // Holdout: labels written after the seed and rules existed, never used to tune them.
    // The main fixture shares many labels with seed-labels.json, so this is the honest local rate.
    // Misses here go to Jev at runtime; the floor only catches a regression in the local path.
    [Fact]
    public void HoldoutLabelsReportLocalPassRate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "form-labels-holdout.json");
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var cache = new LabelCache(null);
        var failures = new List<string>();
        foreach (var row in rows)
        {
            var label = LabelNormalizer.Normalize(row.Label);
            var got = cache.TryGet(label, out var key) ? key : KeywordRules.Match(label) ?? "none";
            if (got != row.Expected) failures.Add($"{row.Source}: {row.Label} expected {row.Expected}, got {got}");
        }
        var passed = rows.Length - failures.Count;
        var pct = passed * 100.0 / rows.Length;
        var report = $"HOLDOUT PASS RATE: {passed}/{rows.Length} = {pct:F1}%";
        output.WriteLine(report);
        foreach (var failure in failures) output.WriteLine(failure);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "holdout-pass-rate.txt"), report + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(pct >= 70, report + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}

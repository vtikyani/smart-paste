using System.Diagnostics;
using System.Text.Json;
using SmartPaste.Core;

const string sample = "Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100";
string? keysPath = null;
var runFixture = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--keys" && i + 1 < args.Length)
        keysPath = args[++i];
    else if (args[i] == "--fixture")
        runFixture = true;
    else
        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
}

var openrouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var typesafeKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if ((string.IsNullOrWhiteSpace(openrouterKey) || string.IsNullOrWhiteSpace(typesafeKey)) && keysPath is not null && File.Exists(keysPath))
{
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(keysPath));
    if (document.RootElement.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
    {
        openrouterKey = string.IsNullOrWhiteSpace(openrouterKey) ? ReadKey(env, "OPENROUTER_API_KEY") : openrouterKey;
        typesafeKey = string.IsNullOrWhiteSpace(typesafeKey) ? ReadKey(env, "TYPESAFE_API_KEY") : typesafeKey;
    }
}
if (string.IsNullOrWhiteSpace(openrouterKey))
    throw new InvalidOperationException("OPENROUTER_API_KEY is required for the sample parse.");

using var http = new HttpClient();
var parser = new OpenRouterParser(http, openrouterKey);
var watch = Stopwatch.StartNew();
var profile = await parser.ParseTextAsync(sample, DateTimeOffset.UtcNow, CancellationToken.None);
watch.Stop();
Console.WriteLine($"LooksMultiField: {TextHeuristics.LooksMultiField(sample)}");
Console.WriteLine($"Parse latency: {watch.ElapsedMilliseconds} ms");
foreach (var entity in profile.Entities)
{
    Console.WriteLine($"[{entity.Label}]");
    foreach (var (key, value) in entity.Fields)
        Console.WriteLine($"  {key}: {value}");
}

var chooser = new TrackingChooser(new JevClient(http, typesafeKey, openrouterKey));
var decider = new FieldDecider(LabelCache.Empty(), chooser, useLocalRules: false);
var labels = new[]
{
    "First Name", "Surname", "Full legal name", "Street address", "Apt, suite, etc. (optional)",
    "Town/City", "State / Province", "ZIP / Postal Code", "Email address", "Mobile phone number"
};
foreach (var label in labels)
{
    chooser.Last = null;
    watch.Restart();
    var decision = await decider.DecideAsync(Context(label), profile, CancellationToken.None);
    watch.Stop();
    Console.WriteLine($"{label} -> {decision.Key ?? "none"} = {decision.Value ?? ""} ({decision.Source}, confidence {chooser.Last?.Confidence.ToString("0.000") ?? "n/a"}, {watch.ElapsedMilliseconds} ms)");
}

if (runFixture)
{
    var fixturePath = Path.Combine("tests", "SmartPaste.Core.Tests", "fixtures", "form-labels.json");
    using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
    var cache = new LabelCache(null);
    var total = 0;
    var misses = 0;
    var passes = 0;
    foreach (var item in fixture.RootElement.EnumerateArray())
    {
        var label = item.GetProperty("label").GetString()!;
        var expected = item.GetProperty("expected").GetString()!;
        var normalized = LabelNormalizer.Normalize(label);
        total++;
        string got;
        if (cache.TryGet(normalized, out var cached))
            got = cached;
        else if (KeywordRules.Match(normalized) is { } ruled)
            got = ruled;
        else
        {
            misses++;
            var decision = await decider.DecideAsync(Context(label), profile, CancellationToken.None);
            got = decision.Key ?? "none";
        }
        if (got == expected)
            passes++;
        else
            Console.WriteLine($"Fixture miss: {label} expected {expected}, got {got}");
    }
    Console.WriteLine($"Local+Jev fixture pass rate: {passes}/{total} = {(total == 0 ? 100 : 100d * passes / total):0.0}% ({misses} local misses sent to Jev)");
}

static string? ReadKey(JsonElement env, string name) =>
    env.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static FieldContext Context(string label) => new(label, null, null, null, "Edit", false, Array.Empty<string>());

sealed class TrackingChooser(IJevChooser inner) : IJevChooser
{
    public JevChoice? Last { get; set; }

    public async Task<JevChoice> ChooseAsync(string state, string instructions, IReadOnlyDictionary<string, string> criteria, CancellationToken ct)
    {
        Last = await inner.ChooseAsync(state, instructions, criteria, ct);
        return Last;
    }
}

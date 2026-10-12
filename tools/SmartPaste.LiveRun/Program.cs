using System.Diagnostics;
using System.Text.Json;
using SmartPaste.Core;

// Developer tool: runs the real parsing model and the Jev-first decider against the live APIs.
// Keys come only from OPENROUTER_API_KEY / TYPESAFE_API_KEY, or from a JSON file passed with --keys
// (top-level properties, or an "env" object, with those two names).
// Usage: SmartPaste.LiveRun [--keys <file>] [--route auto|typesafe|openrouter] [--model <openrouter model id>] [--fixture]

const string sample = "Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100";
var jevDeadline = TimeSpan.FromSeconds(10);

string? keysPath = null;
string? model = null;
var route = JevRoutePreference.Auto;
var runFixture = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--keys" when i + 1 < args.Length:
            keysPath = args[++i];
            break;
        case "--model" when i + 1 < args.Length:
            model = args[++i];
            break;
        case "--route" when i + 1 < args.Length && Enum.TryParse<JevRoutePreference>(args[i + 1], true, out var parsed):
            route = parsed;
            i++;
            break;
        case "--fixture":
            runFixture = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            return 2;
    }
}

var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var typeSafeKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if (keysPath is not null)
{
    if (!File.Exists(keysPath))
    {
        Console.Error.WriteLine("The --keys file does not exist.");
        return 2;
    }
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(keysPath));
    var root = document.RootElement.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object ? env : document.RootElement;
    openRouterKey = string.IsNullOrWhiteSpace(openRouterKey) ? ReadKey(root, "OPENROUTER_API_KEY") : openRouterKey;
    typeSafeKey = string.IsNullOrWhiteSpace(typeSafeKey) ? ReadKey(root, "TYPESAFE_API_KEY") : typeSafeKey;
}

using var http = new HttpClient();
Console.WriteLine($"OpenRouter key: {(string.IsNullOrWhiteSpace(openRouterKey) ? "missing" : "set")}, TypeSafe key: {(string.IsNullOrWhiteSpace(typeSafeKey) ? "missing" : "set")}");
if (!string.IsNullOrWhiteSpace(openRouterKey))
    Console.WriteLine($"OpenRouter key check: {(await ConnectionChecks.CheckOpenRouterKeyAsync(http, openRouterKey, CancellationToken.None)).Message}");
if (!string.IsNullOrWhiteSpace(typeSafeKey))
    Console.WriteLine($"TypeSafe key check: {(await ConnectionChecks.CheckTypeSafeKeyAsync(http, typeSafeKey, CancellationToken.None)).Message}");

// ---- Parsing: local first, then the model ----
Console.WriteLine($"LooksMultiField: {TextHeuristics.LooksMultiField(sample)}");
var watch = Stopwatch.StartNew();
var profile = LocalExtractor.Extract(sample, DateTimeOffset.UtcNow);
Console.WriteLine($"Local extraction: {watch.Elapsed.TotalMilliseconds:0.0} ms");
Print(profile);
if (!string.IsNullOrWhiteSpace(openRouterKey))
{
    var parser = new OpenRouterParser(http, openRouterKey, model ?? OpenRouterParser.DefaultModel);
    watch.Restart();
    try
    {
        var parsed = await parser.ParseTextAsync(sample, DateTimeOffset.UtcNow, CancellationToken.None);
        Console.WriteLine($"Model parse ({parser.Model}): {watch.ElapsedMilliseconds} ms");
        profile = ProfileParser.Merge(parsed, profile);
        Print(profile);
    }
    catch (ServiceException ex)
    {
        Console.WriteLine($"Model parse failed: {ex.Failure} ({ex.StatusCode?.ToString() ?? "no status"}); using the local result.");
    }
}

// ---- Jev decides ----
var credentials = new JevCredentials(typeSafeKey, openRouterKey, route);
IJevChooser? jev = credentials.HasKey ? new JevClient(http, credentials) : null;
if (jev is null) Console.WriteLine("No Jev key: decisions below use the offline fallback.");
var decider = new FieldDecider(LabelCache.Empty(), jev);
string[] labels =
[
    "First Name", "Surname", "Full legal name", "Street address", "Apt, suite, etc. (optional)", "Town/City",
    "State / Province", "ZIP / Postal Code", "Email address", "Mobile phone number", "Emergency contact phone"
];
foreach (var label in labels)
{
    watch.Restart();
    var decision = await decider.DecideAsync(Field(label), profile, jevDeadline, CancellationToken.None);
    Console.WriteLine($"{label} -> {Describe(decision)} [{watch.ElapsedMilliseconds} ms]");
}

if (runFixture)
{
    foreach (var fixture in new[] { "form-labels.json", "form-labels-regression.json" })
    {
        var path = Path.Combine("tests", "SmartPaste.Core.Tests", "fixtures", fixture);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Run from the repository root: {path} not found.");
            return 2;
        }
        // Every standard key has a value, so Jev chooses among all field types.
        var everything = new Profile(
            [new Entity("person", FieldKeys.Standard.ToDictionary(key => key, ExampleValue))], DateTimeOffset.UtcNow);
        using var rows = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        int total = 0, passes = 0, pickers = 0, byJev = 0;
        foreach (var row in rows.RootElement.EnumerateArray())
        {
            var label = row.GetProperty("label").GetString()!;
            var expected = row.GetProperty("expected").GetString()!;
            var decision = await decider.DecideAsync(Field(label), everything, jevDeadline, CancellationToken.None);
            total++;
            if (decision.DecidedBy == DecisionSource.Jev) byJev++;
            if (decision.Kind == DecisionKind.Picker) pickers++;
            var got = decision.Kind == DecisionKind.Value ? decision.Key! : decision.Kind == DecisionKind.Picker ? "picker" : "none";
            if (got == expected) passes++;
            else Console.WriteLine($"Miss: {label} expected {expected}, got {got} ({Describe(decision)})");
        }
        Console.WriteLine($"{fixture}: {passes}/{total} = {100.0 * passes / Math.Max(1, total):0.0}% ({byJev} decided by Jev, {pickers} sent to the picker)");
    }
}
return 0;

static string? ReadKey(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static FieldContext Field(string label) => new(label, null, null, null, "Edit", false, []);

static string Describe(Decision decision) =>
    $"{decision.Kind} {decision.Key ?? "-"} = {decision.Value ?? string.Join(" | ", decision.Candidates.Select(c => c.Value))} " +
    $"(by {decision.DecidedBy}, {decision.Reason}, confidence {decision.Confidence?.ToString("0.000") ?? "n/a"}" +
    $"{(decision.JevFailure is { } failure ? $", Jev failed: {failure}" : "")})";

static void Print(Profile profile)
{
    foreach (var entity in profile.Entities)
    {
        Console.WriteLine($"  [{entity.Label}]");
        foreach (var (key, value) in entity.Fields) Console.WriteLine($"    {key}: {value}");
    }
}

// Plausible values for the fixture run, so Jev sees realistic options.
static string ExampleValue(string key) => key switch
{
    FieldKeys.FirstName => "Jordan",
    FieldKeys.MiddleName => "Lee",
    FieldKeys.LastName => "Rivera",
    FieldKeys.FullName => "Jordan Lee Rivera",
    FieldKeys.Street => "123 Main St",
    FieldKeys.Unit => "Apt 4B",
    FieldKeys.City => "Springfield",
    FieldKeys.State => "NJ",
    FieldKeys.Zip => "07081",
    FieldKeys.Country => "United States",
    FieldKeys.Email => "jordan.rivera@example.com",
    FieldKeys.Phone => "201-555-0100",
    FieldKeys.Company => "Acme Corp",
    FieldKeys.Title => "Senior Engineer",
    FieldKeys.DateOfBirth => "March 4, 1990",
    FieldKeys.Website => "https://jordanrivera.example.com",
    FieldKeys.Linkedin => "https://www.linkedin.com/in/jordanrivera",
    _ => key
};

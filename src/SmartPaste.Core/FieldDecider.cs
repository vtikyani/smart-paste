namespace SmartPaste.Core;

public enum DecisionKind { Value, Picker, PlainPaste }
public sealed record Candidate(string Key, string EntityLabel, string Value)
{
    public string KeyDisplayName => FieldKeys.DisplayName(Key);
}
public sealed record Decision(DecisionKind Kind, string? Key, string? Value, IReadOnlyList<Candidate> Candidates, string Source, string Reason);
public sealed class FieldDecider
{
    public const double ConfidenceFloor = 0.8;
    private const string Instructions = "Which profile value should be pasted into this form field? Pick none if the field asks for something not in the profile.";
    private readonly LabelCache _cache;
    private readonly IJevChooser? _jev;
    private readonly bool _useLocalRules;

    public FieldDecider(LabelCache cache, IJevChooser? jev, bool useLocalRules = true)
    {
        _cache = cache;
        _jev = jev;
        _useLocalRules = useLocalRules;
    }

    public async Task<Decision> DecideAsync(FieldContext field, Profile profile, CancellationToken ct)
    {
        if (field.IsPassword) return Plain("none", "password");
        if (!field.HasUsableLabel) return Plain("none", "no label");
        var label = LabelNormalizer.Normalize(field.BestLabel());
        if (KeywordRules.IsSensitive(label)) return Plain("none", "sensitive label");
        if (_useLocalRules && _cache.TryGet(label, out var cachedKey)) return Resolve(cachedKey, "cache", label, field, profile);
        if (_useLocalRules && KeywordRules.Match(label) is { } ruleKey) return Resolve(ruleKey, "rules", label, field, profile);
        if (_jev is null) return Plain("none", "no match");

        var criteria = profile.Keys.ToDictionary(k => k,
            k => string.Join(" / ", profile.ValuesFor(k).Select(v => Cut(v.Value, 60))), StringComparer.Ordinal);
        criteria["none"] = "No value in the profile belongs in this field";
        JevChoice choice;
        try { choice = await _jev.ChooseAsync(field.DescribeForJev(), Instructions, criteria, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is JevException or TimeoutException or OperationCanceledException)
        { return Plain("none", "jev error"); }

        if (choice.Confidence < ConfidenceFloor)
        {
            var keys = choice.Probabilities.Where(p => p.Key != "none" && profile.Keys.Contains(p.Key))
                .OrderByDescending(p => p.Value).Take(3).Select(p => p.Key);
            var candidates = keys.SelectMany(k => Candidates(k, field, profile)).ToArray();
            return candidates.Length > 0 ? new(DecisionKind.Picker, null, null, candidates, "jev", "low confidence") : Plain("jev", "low confidence");
        }
        if (choice.Choice == "none") return Plain("jev", "none selected");
        if (!profile.Keys.Contains(choice.Choice)) return Plain("jev", "key absent");
        var decision = Resolve(choice.Choice, "jev", label, field, profile);
        if (decision.Kind == DecisionKind.Value) _cache.Set(label, choice.Choice);
        return decision;
    }

    private static Decision Resolve(string key, string source, string label, FieldContext field, Profile profile)
    {
        var candidates = Candidates(key, field, profile);
        if (candidates.Count == 0) return Plain(source, "key absent");
        if (candidates.Count == 1) return new(DecisionKind.Value, key, candidates[0].Value, candidates, source, "matched");
        var words = new[] { "billing", "shipping", "home", "work", "mailing", "company" };
        var matches = candidates.Where(c => words.Any(w => Has(label, w) && Has(LabelNormalizer.Normalize(c.EntityLabel), w))).ToArray();
        if (matches.Length == 1) return new(DecisionKind.Value, key, matches[0].Value, candidates, source, "entity matched");
        return new(DecisionKind.Picker, key, null, candidates, source, "multiple values");
    }

    private static IReadOnlyList<Candidate> Candidates(string key, FieldContext field, Profile profile) =>
        profile.ValuesFor(key).DistinctBy(v => v.Value)
            .Select(v => new Candidate(key, v.Entity.Label, ValueFormatter.Format(key, v.Value, field))).ToArray();

    private static bool Has(string value, string word) => (" " + value + " ").Contains(" " + word + " ", StringComparison.Ordinal);
    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
    private static Decision Plain(string source, string reason) => new(DecisionKind.PlainPaste, null, null, Array.Empty<Candidate>(), source, reason);
}

namespace SmartPaste.Core;

public enum DecisionKind { Value, Picker, PlainPaste }

// Who made a decision.
public enum DecisionSource
{
    // Nothing decided: the field gets a plain paste before anything is asked (password, sensitive, no label).
    None,
    // TypeSafe's Jev: the normal case.
    Jev,
    // A label learned on this PC (fallback when Jev is unavailable).
    Learned,
    // The built-in label list (fallback).
    BuiltIn,
    // Keyword rules (fallback).
    Rules
}

public sealed record Candidate(string Key, string EntityLabel, string Value)
{
    public string KeyDisplayName => FieldKeys.DisplayName(Key);
}

// Why a decision came out the way it did. Short snake_case tokens: the app logs them.
public static class DecisionReasons
{
    public const string Matched = "matched";
    public const string EntityMatched = "entity_matched";
    public const string MultipleValues = "multiple_values";
    public const string NoneSelected = "none_selected";
    public const string LowConfidence = "low_confidence";
    public const string JevUnavailable = "jev_unavailable";
    public const string SensitiveLabel = "sensitive_label";
    public const string Password = "password";
    public const string NoLabel = "no_label";
    public const string KeyAbsent = "key_absent";
    public const string NoMatch = "no_match";
}

// What Ctrl+V does in one field. Source ("jev", "learned", "builtin", "rules", "none") and Reason are log-safe tokens.
public sealed record Decision(
    DecisionKind Kind, string? Key, string? Value, IReadOnlyList<Candidate> Candidates, string Source, string Reason)
{
    public DecisionSource DecidedBy { get; init; }

    // Jev's confidence, when Jev answered.
    public double? Confidence { get; init; }

    // Why Jev did not decide, when it could not (no key, offline, an error). The app shows it as service status.
    public ServiceFailure? JevFailure { get; init; }
}

// Decides what a field gets from the armed block. Jev decides every paste; the local label list (learned labels,
// built-in list, keyword rules) decides only when Jev is unavailable.
public sealed class FieldDecider
{
    // TypeSafe's guidance: act automatically only above 0.8; otherwise let the user choose.
    public const double ConfidenceFloor = 0.8;

    // Jev's confidence measures how concentrated its answer is, not how likely the chosen value is: 0.52 for the full
    // name and 0.48 for the first name can still be "confident". The chosen value must also be at least this
    // probable to be pasted without asking.
    public const double ChoiceProbabilityFloor = 0.7;

    public const int PickerSize = 3;

    // Deadline for Jev at paste time, shared by all routes. Ask-ahead callers pass a longer one.
    public static readonly TimeSpan PasteDeadline = TimeSpan.FromMilliseconds(1500);

    // Words in a label or section that name the entity a value belongs to, and the word in that entity's label.
    private static readonly (string Entity, string[] Words)[] EntityWords =
    [
        ("billing", ["billing"]),
        ("shipping", ["shipping", "delivery"]),
        ("home", ["home"]),
        ("work", ["work"]),
        ("mailing", ["mailing"]),
        ("company", ["company", "employer", "organization", "organisation", "business"])
    ];

    // A value at least this long counts inside a longer label too ("Hi Jordan Rivera"); shorter ones ("NJ", "4")
    // are too common as words.
    private const int ValueInLabelMinLength = 3;

    private readonly LabelCache _cache;
    private readonly IJevChooser? _jev;
    private readonly bool _useLocalRules;

    // jev is null when no key is saved. useLocalRules false turns the offline fallback off (to measure Jev alone).
    public FieldDecider(LabelCache cache, IJevChooser? jev, bool useLocalRules = true)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _jev = jev;
        _useLocalRules = useLocalRules;
    }

    public Task<Decision> DecideAsync(FieldContext field, Profile profile, CancellationToken ct) =>
        DecideAsync(field, profile, PasteDeadline, ct);

    public async Task<Decision> DecideAsync(FieldContext field, Profile profile, TimeSpan jevDeadline, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(profile);
        // These never reach Jev or anything else.
        if (field.IsPassword) return Plain(DecisionReasons.Password);
        if (KeywordRules.IsSensitive(field)) return Plain(DecisionReasons.SensitiveLabel);
        if (!field.HasContext) return Plain(DecisionReasons.NoLabel);
        if (profile.Keys.Count == 0) return Plain(DecisionReasons.KeyAbsent);

        ServiceFailure failure;
        if (_jev is null)
        {
            failure = ServiceFailure.NoKey;
        }
        else
        {
            var question = JevQuestion.ForField(field, profile);
            // Only "none" to offer: every value is blank.
            if (question.Options.Count == 1) return Plain(DecisionReasons.KeyAbsent);
            try
            {
                var choice = await _jev.ChooseAsync(question, jevDeadline, ct).ConfigureAwait(false);
                return FromJev(choice, question, field, profile);
            }
            catch (ServiceException ex)
            {
                failure = ex.Failure;
            }
        }

        var fallback = _useLocalRules ? DecideLocally(field, profile) : Plain(DecisionReasons.JevUnavailable);
        return fallback with { JevFailure = failure };
    }

    // The user chose a value in the picker: remember the label so the offline fallback knows it next time. Without
    // the profile only the chosen value is kept from being learned as a label; prefer the overload with the profile.
    public void LearnChoice(FieldContext field, Candidate chosen)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(chosen);
        Learn(field, chosen.Key, [chosen.Value]);
    }

    // As above. A label that matches any value of the armed block (a name shown next to the field) is never learned,
    // because the label cache is saved to disk.
    public void LearnChoice(FieldContext field, Candidate chosen, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(profile);
        Learn(field, chosen.Key, AllValues(profile).Append(chosen.Value));
    }

    // Every distinct value of the block, standard keys first, formatted for the field when one is given.
    // For the picker shortcut.
    public static IReadOnlyList<Candidate> AllCandidates(Profile profile, FieldContext? field)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var candidates = new List<Candidate>();
        foreach (var key in profile.Keys)
            foreach (var (entity, value) in JevQuestion.DistinctValues(profile, key))
                candidates.Add(new Candidate(key, entity.Label, field is null ? value : ValueFormatter.Format(key, value, field)));
        return candidates;
    }

    private Decision FromJev(JevChoice choice, JevQuestion question, FieldContext field, Profile profile)
    {
        // Jev finds "none" most likely: a normal paste, however unsure it is about the rest. Offering the user's values
        // here would invite pasting them where they do not belong (an emergency contact's email).
        if (NoneIsMostLikely(choice))
            return Plain(DecisionReasons.NoneSelected) with { Source = "jev", DecidedBy = DecisionSource.Jev, Confidence = choice.Confidence };

        if (choice.Confidence > ConfidenceFloor && Probability(choice, choice.Choice) >= ChoiceProbabilityFloor &&
            question.Option(choice.Choice)?.Candidate is { } picked)
        {
            var value = Formatted(picked, field);
            Learn(field, value.Key, AllValues(profile).Append(value.Value));
            return new Decision(DecisionKind.Value, value.Key, value.Value, [value], "jev", DecisionReasons.Matched)
            {
                DecidedBy = DecisionSource.Jev,
                Confidence = choice.Confidence
            };
        }

        // Unsure: Jev's most probable values (never "none") for the user to choose from.
        var order = question.Options.Select((option, index) => (option.Name, index)).ToDictionary(p => p.Name, p => p.index);
        var ranked = choice.Probabilities
            .OrderByDescending(p => p.Value).ThenBy(p => order.GetValueOrDefault(p.Key, int.MaxValue))
            .Select(p => p.Key).ToList();
        if (!ranked.Contains(choice.Choice)) ranked.Insert(0, choice.Choice);
        var candidates = Distinct(ranked
            .Select(name => question.Option(name)?.Candidate)
            .OfType<Candidate>()
            .Select(candidate => Formatted(candidate, field)))
            .Take(PickerSize).ToArray();
        if (candidates.Length == 0)
            return Plain(DecisionReasons.LowConfidence) with { Source = "jev", DecidedBy = DecisionSource.Jev, Confidence = choice.Confidence };
        return new Decision(DecisionKind.Picker, SharedKey(candidates), null, candidates, "jev", DecisionReasons.LowConfidence)
        {
            DecidedBy = DecisionSource.Jev,
            Confidence = choice.Confidence
        };
    }

    // "none" has the highest probability. A tie goes to Jev's own choice; without probabilities, its choice decides.
    private static bool NoneIsMostLikely(JevChoice choice)
    {
        if (!choice.Probabilities.TryGetValue(JevQuestion.NoneOption, out var none)) return choice.IsNone;
        var best = choice.Probabilities.Where(p => p.Key != JevQuestion.NoneOption).Select(p => p.Value).DefaultIfEmpty(0).Max();
        return none > best || (none == best && choice.IsNone);
    }

    // The probability Jev gave an option. An answer without one is never pasted unasked: its confidence alone cannot
    // say how likely the chosen value is.
    private static double Probability(JevChoice choice, string option) => choice.Probabilities.GetValueOrDefault(option);

    // The offline fallback: learned labels, then the built-in list, then keyword rules.
    private Decision DecideLocally(FieldContext field, Profile profile)
    {
        var label = LabelNormalizer.Normalize(field.BestLabel());
        // A field about someone else (spouse, emergency contact, reference) must not get the user's own details.
        if (label.Length == 0 || IsAboutSomeoneElse(field)) return Plain(DecisionReasons.NoMatch);
        if (_cache.TryGet(label, out var key, out var learned))
            return Resolve(key, learned ? DecisionSource.Learned : DecisionSource.BuiltIn, field, profile);
        if (KeywordRules.Match(label) is { } ruleKey) return Resolve(ruleKey, DecisionSource.Rules, field, profile);
        return Plain(DecisionReasons.NoMatch);
    }

    private static Decision Resolve(string key, DecisionSource source, FieldContext field, Profile profile)
    {
        var token = SourceToken(source);
        var candidates = Distinct(JevQuestion.DistinctValues(profile, key)
            .Select(v => Formatted(new Candidate(key, v.Entity.Label, v.Value), field))).ToArray();
        if (candidates.Length == 0) return Plain(DecisionReasons.KeyAbsent) with { Source = token, DecidedBy = source };
        if (candidates.Length == 1)
            return new Decision(DecisionKind.Value, key, candidates[0].Value, candidates, token, DecisionReasons.Matched) { DecidedBy = source };

        // Several values (billing vs shipping): the label, then the nearest section, may name the one to use.
        foreach (var context in field.Sections.Prepend(field.BestLabel()))
        {
            var text = LabelNormalizer.Normalize(context);
            var matches = candidates.Where(c => EntityWords.Any(entity =>
                entity.Words.Any(word => Has(text, word)) && Has(LabelNormalizer.Normalize(c.EntityLabel), entity.Entity))).ToArray();
            if (matches.Length == 1)
                return new Decision(DecisionKind.Value, key, matches[0].Value, candidates, token, DecisionReasons.EntityMatched) { DecidedBy = source };
        }
        return new Decision(DecisionKind.Picker, key, null, candidates, token, DecisionReasons.MultipleValues) { DecidedBy = source };
    }

    // Any text that names the field, or a section it sits in, says it belongs to another person.
    private static bool IsAboutSomeoneElse(FieldContext field) =>
        new[] { field.BestLabel(), field.LabeledBy, field.Name, field.Placeholder, field.MeaningfulAutomationId, field.OcrText }
            .Concat(field.Siblings).Concat(field.Sections)
            .Any(text => !string.IsNullOrWhiteSpace(text) && KeywordRules.IsAboutSomeoneElse(text!));

    // Only text the app labels the field with is learned (never an id, help text, or OCR), never one of the block's
    // values, never from a field that is sensitive by its context, and only labels the cache accepts (not built in,
    // not vague, not sensitive; see LabelCache.IsLearnable).
    private void Learn(FieldContext field, string key, IEnumerable<string> values)
    {
        if (field.LearnableLabel() is { } label && !IsOneOfTheValues(label, values) && !KeywordRules.IsSensitive(field))
            _cache.Set(label, key);
    }

    private static bool IsOneOfTheValues(string label, IEnumerable<string> values)
    {
        var text = LabelNormalizer.Normalize(label);
        foreach (var value in values)
        {
            var normalized = LabelNormalizer.Normalize(value);
            if (normalized.Length == 0) continue;
            if (normalized == text || (normalized.Length >= ValueInLabelMinLength && Has(text, normalized))) return true;
        }
        return false;
    }

    private static IEnumerable<string> AllValues(Profile profile) => profile.Entities.SelectMany(e => e.Fields.Values);

    private static Candidate Formatted(Candidate candidate, FieldContext field) =>
        candidate with { Value = ValueFormatter.Format(candidate.Key, candidate.Value, field) };

    // Two values can format to the same text ("NJ" and "New Jersey" in a full-name field): show it once.
    private static IEnumerable<Candidate> Distinct(IEnumerable<Candidate> candidates) =>
        candidates.DistinctBy(c => c.Value, StringComparer.Ordinal);

    private static string? SharedKey(IReadOnlyList<Candidate> candidates) =>
        candidates.All(c => c.Key == candidates[0].Key) ? candidates[0].Key : null;

    private static string SourceToken(DecisionSource source) => source switch
    {
        DecisionSource.Jev => "jev",
        DecisionSource.Learned => "learned",
        DecisionSource.BuiltIn => "builtin",
        DecisionSource.Rules => "rules",
        _ => "none"
    };

    private static bool Has(string value, string word) => (" " + value + " ").Contains(" " + word + " ", StringComparison.Ordinal);

    private static Decision Plain(string reason) =>
        new(DecisionKind.PlainPaste, null, null, Array.Empty<Candidate>(), "none", reason) { DecidedBy = DecisionSource.None };
}

using System.Net.Http;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// The practice form in the welcome tour. Runs the real pipeline in-process (Smart Paste never intercepts its own
// windows): the copied text is read like any copy, and the field is decided like any field, by Jev when a key is
// saved and by the local label list otherwise.
internal sealed class PracticeEngine(HttpClient http, DecisionService decisions, SettingsStore settings) : IPracticeEngine
{
    // One block is pasted into several practice fields; it is read once.
    private (string Text, Profile Profile)? _last;

    public async Task<PracticeResult?> PasteIntoAsync(string fieldLabel, string clipboardText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clipboardText) || string.IsNullOrWhiteSpace(fieldLabel)) return null;
        var profile = await ReadAsync(clipboardText, ct);
        if (profile.Keys.Count == 0) return null;

        var field = new FieldContext(fieldLabel, null, null, fieldLabel, "Edit", false, []);
        var decision = await decisions.DecideAsync(field, profile, FieldDecider.PasteDeadline, ct);
        // The practice form has no picker: when two values fit, the most likely one is shown.
        var pick = decision.Kind switch
        {
            DecisionKind.Value when decision.Key is { } key && decision.Value is { } value => new Candidate(key, "", value),
            DecisionKind.Picker => decision.Candidates.FirstOrDefault(),
            _ => null
        };
        if (pick is null) return null;
        var decidedBy = decision.DecidedBy switch
        {
            DecisionSource.Jev => nameof(DecisionMaker.Jev),
            DecisionSource.Learned => nameof(DecisionMaker.Learned),
            _ => nameof(DecisionMaker.BuiltIn)
        };
        return new PracticeResult(pick.Value, FieldKeys.DisplayName(pick.Key), decidedBy);
    }

    private async Task<Profile> ReadAsync(string text, CancellationToken ct)
    {
        if (_last is { } last && last.Text == text) return last.Profile;
        var profile = LocalExtractor.Extract(text, DateTimeOffset.UtcNow);
        if (CredentialStore.Get(CredentialStore.OpenRouter) is { } key && text.Length <= TextHeuristics.MaxChars)
        {
            try
            {
                var parsed = await new OpenRouterParser(http, key, settings.Current.ParsingModel).ParseTextAsync(text, DateTimeOffset.UtcNow, ct);
                profile = ProfileParser.Merge(parsed, profile);
            }
            catch (ServiceException ex)
            {
                // The local reading is enough to practise with.
                Log.Event("practice_parse_error", ("reason", ex.Failure.ToString()));
            }
        }
        _last = (text, profile);
        return profile;
    }
}

using System.Net.Http;
using SmartPaste.App.Infrastructure;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// The welcome tour's sample contact block: the only text the practice form ever works with. Tutorial content, with
// reserved example values: example.com addresses and 555-01xx phone numbers never belong to a real person.
internal static class PracticeSample
{
    public const string Text = "Jordan Rivera\njordan.rivera@example.com\n(201) 555-0143\n48 Maple Avenue\nHoboken, NJ 07030";

    // Whether clipboard text is the sample as the tour's Copy button put it there (line endings may come back
    // changed). Anything else is the user's own text, like an API key copied a moment ago.
    public static bool Is(string? text) =>
        text is not null && text.Replace("\r\n", "\n").Replace('\r', '\n').Trim() == Text;
}

// The practice form in the welcome tour. Runs the real pipeline in-process (Smart Paste never intercepts its own
// windows): the sample is read like any copy, and the field is decided like any field, by Jev when a key is saved and
// by the local label list otherwise. It works on the sample only: other text never goes to OpenRouter or Jev from
// here. One engine serves one run of the tour; disposing it when the tour closes drops what it read.
internal sealed class PracticeEngine(HttpClient http, DecisionService decisions, SettingsStore settings) : IPracticeEngine, IDisposable
{
    private readonly object _gate = new();
    // The sample, read once for every practice field.
    private Profile? _sample;
    private bool _disposed;

    public async Task<PracticeResult?> PasteIntoAsync(string fieldLabel, string clipboardText, CancellationToken ct)
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PracticeSample.Is(clipboardText) || string.IsNullOrWhiteSpace(fieldLabel)) return null;
        var profile = await ReadSampleAsync(ct);
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

    private async Task<Profile> ReadSampleAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_sample is { } read) return read;
        }
        var profile = LocalExtractor.Extract(PracticeSample.Text, DateTimeOffset.UtcNow);
        if (CredentialStore.Get(CredentialStore.OpenRouter) is { } key)
        {
            try
            {
                var parsed = await new OpenRouterParser(http, key, settings.Current.ParsingModel).ParseTextAsync(PracticeSample.Text, DateTimeOffset.UtcNow, ct);
                profile = ProfileParser.Merge(parsed, profile);
            }
            catch (ServiceException ex)
            {
                // The local reading is enough to practise with.
                Log.Event("practice_parse_error", ("reason", ex.Failure.ToString()));
            }
        }
        lock (_gate)
        {
            if (!_disposed) _sample = profile;
        }
        return profile;
    }

    // The tour closed.
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _sample = null;
        }
    }
}

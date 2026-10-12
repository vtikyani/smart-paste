using SmartPaste.App.Services;

// A clock that stays where a test puts it.
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}

// Remembers which windows the UI asked to open.
internal sealed class RecordingNavigator : IWindowNavigator
{
    public List<string?> SettingsPagesOpened { get; } = [];

    public int OnboardingOpened { get; private set; }

    public void ShowSettings(string? page = null) => SettingsPagesOpened.Add(page);

    public void ShowOnboarding() => OnboardingOpened++;
}

// Stands in for the keyboard hook: a test presses keys through the active sink exactly as the hook would, with
// Accepts deciding between delivering the key (OnKey) and letting it through to the app (OnOtherKey).
internal sealed class ManualKeyCapture : IKeyCapture
{
    public IKeySink? Sink { get; private set; }

    public int Captures { get; private set; }

    public IDisposable Capture(IKeySink sink)
    {
        Sink = sink;
        Captures++;
        return new Release(this, sink);
    }

    // Returns true when the sink took the key.
    public bool Press(int virtualKey, char? character = null, KeyModifiers modifiers = KeyModifiers.None)
    {
        var sink = Sink ?? throw new InvalidOperationException("Nothing is capturing keys.");
        var key = new KeyStroke(virtualKey, character, modifiers, IsRepeat: false);
        if (sink.Accepts(key))
        {
            sink.OnKey(key);
            return true;
        }
        sink.OnOtherKey();
        return false;
    }

    // As the hook does when keys come faster than the UI thread handles them: every key is offered (Accepts) before
    // the first one is handled; then each is handled in order, unless the capture has ended by then. Returns which
    // keys the sink took.
    public bool[] PressQuickly(params KeyStroke[] keys)
    {
        var sink = Sink ?? throw new InvalidOperationException("Nothing is capturing keys.");
        var accepted = keys.Select(key => sink.Accepts(key)).ToArray();
        for (var i = 0; i < keys.Length; i++)
        {
            if (!ReferenceEquals(Sink, sink)) break;
            if (accepted[i]) sink.OnKey(keys[i]);
            else sink.OnOtherKey();
        }
        return accepted;
    }

    private sealed class Release(ManualKeyCapture owner, IKeySink sink) : IDisposable
    {
        public void Dispose()
        {
            if (owner.Sink == sink) owner.Sink = null;
        }
    }
}

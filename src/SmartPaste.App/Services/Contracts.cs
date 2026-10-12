using SmartPaste.Core;

namespace SmartPaste.App.Services;

// A rectangle in physical screen pixels on the virtual desktop (what UI Automation and Win32 report).
// WPF windows work in device-independent units, so position windows from this with SetWindowPos, not Left/Top.
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static ScreenRect FromLTRB(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
    public static ScreenRect FromPoint(int x, int y) => new(x, y, 1, 1);
}

// ---- Paste feedback: implemented by the UI, called by the paste service on the UI thread ----

public enum PickMode
{
    // Jev was unsure, or two values fit (billing vs shipping): a short list to choose from.
    Ambiguous,
    // The picker shortcut: every value of the armed block, filterable by typing.
    AllValues
}

public sealed record PickRequest(IReadOnlyList<Candidate> Candidates, PickMode Mode, ScreenRect Anchor, string? FieldLabel);

public enum PickOutcome
{
    // The user chose a value.
    Chosen,
    // Escape or "Paste original": paste the clipboard as it is.
    PasteOriginal,
    // Clicked elsewhere, typed something else, or timed out: paste nothing.
    Dismissed
}

public sealed record PickResult(PickOutcome Outcome, Candidate? Candidate)
{
    public static readonly PickResult Original = new(PickOutcome.PasteOriginal, null);
    public static readonly PickResult Dismissed = new(PickOutcome.Dismissed, null);
    public static PickResult Chose(Candidate candidate) => new(PickOutcome.Chosen, candidate);
}

// What Ctrl+V will paste into the focused field, shown next to it when field hints are on.
public sealed record FieldHint(string KeyDisplayName, string ValuePreview, bool NeedsChoice);

public interface IPasteFeedback
{
    // Small "working" indicator near the caret while a decision takes longer than a moment.
    void ShowWorking(ScreenRect anchor);
    void HideWorking();

    // Brief confirmation after a smart paste, for example "First name".
    void ShowPasted(ScreenRect anchor, string what);

    void ShowFieldHint(ScreenRect field, FieldHint hint);
    void HideFieldHint();

    // Shows the picker without taking focus from the target app (keys arrive through IKeyCapture).
    // Completes when the user chooses, presses Escape, dismisses it, or ct is canceled (-> Dismissed).
    Task<PickResult> PickAsync(PickRequest request, CancellationToken ct);
}

// ---- Keyboard capture: implemented by the keyboard hook, used by the picker ----

[Flags]
public enum KeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Windows = 8 }

// One key press. Character is what the key types with the target app's keyboard layout, when it types one.
public readonly record struct KeyStroke(int VirtualKey, char? Character, KeyModifiers Modifiers, bool IsRepeat);

public interface IKeySink
{
    // Hook thread. Must return within microseconds and be thread-safe: true swallows the key and delivers it
    // to OnKey; false lets it through to the focused app and calls OnOtherKey.
    bool Accepts(in KeyStroke key);

    // UI thread: an accepted key press.
    void OnKey(KeyStroke key);

    // UI thread: a key press the sink did not accept went through to the app (the picker usually closes).
    void OnOtherKey();

    // UI thread: a mouse button went down anywhere on screen (physical pixels). Never swallowed.
    void OnPointerDown(int x, int y);
}

public interface IKeyCapture
{
    // Routes key presses to the sink until the returned handle is disposed. Only one capture is active at a
    // time; a new capture replaces the previous one.
    IDisposable Capture(IKeySink sink);
}

// ---- Updates: implemented with Velopack ----

public abstract class AvailableUpdate
{
    public abstract string Version { get; }
    public abstract string? ReleaseNotesMarkdown { get; }
}

public interface IUpdateService
{
    // False when running from a build folder rather than an installed copy; updates are then unavailable.
    bool IsSupported { get; }

    string CurrentVersion { get; }

    Task<AvailableUpdate?> CheckAsync(CancellationToken ct);

    Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress, CancellationToken ct);

    // Exits the app, installs the downloaded update, and starts the new version.
    void ApplyAndRestart(AvailableUpdate update);
}

// ---- Connection checks for the settings page and the welcome tour ----

public sealed record ConnectionCheck(bool Ok, string Message);

public interface IConnectionTester
{
    Task<ConnectionCheck> CheckOpenRouterAsync(string apiKey, CancellationToken ct);
    Task<ConnectionCheck> CheckTypeSafeAsync(string apiKey, CancellationToken ct);
}

// ---- The practice form in the welcome tour ----

public interface IPracticeEngine
{
    // Runs the real decision pipeline in-process: parses clipboardText the way a copy would, then decides what
    // a field with this label gets. Null when nothing fits.
    Task<PracticeResult?> PasteIntoAsync(string fieldLabel, string clipboardText, CancellationToken ct);
}

public sealed record PracticeResult(string Value, string KeyDisplayName, string DecidedBy);

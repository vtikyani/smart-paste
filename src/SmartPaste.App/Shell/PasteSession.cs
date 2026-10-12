namespace SmartPaste.App.Shell;

// Where a paste goes: the foreground window, its process, and the process of the focused element (different for
// UWP apps, whose window belongs to ApplicationFrameHost).
internal sealed record PasteTarget(IntPtr Window, uint WindowProcessId, uint FocusProcessId);

internal enum PasteOutcome
{
    // The target read the value; the original clipboard is back.
    Consumed,

    // The target never read the value within the time limit; the original clipboard is back.
    NotConsumed,

    // Something new was copied meanwhile; it was left alone.
    Superseded,

    // The value could not be put on the clipboard; a plain paste of the original clipboard was sent instead.
    Failed,

    // The target window lost the foreground before anything was sent; nothing was pasted.
    TargetChanged
}

// Who asked for the delayed-rendered value.
internal enum Reader
{
    // The target app's window or focused-element process.
    Target,

    // A reader that opened the clipboard without a window: most likely the target, so it counts as one.
    Unidentified,

    // Any other process, typically a clipboard manager: it gets the value but does not count as the paste.
    Other
}

internal enum RestoreResult
{
    Restored,

    // We no longer own the clipboard: someone else wrote to it, and their data stays.
    LeftAlone,

    Failed
}

// The decision side of one smart paste: who read the value, whether it was superseded, and what that means. The
// clipboard calls live in ClipboardPaster.
internal sealed class PasteSession
{
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PasteSession(string value, PasteTarget target)
    {
        Value = value;
        Target = target;
    }

    public string Value { get; }

    public PasteTarget Target { get; }

    public bool Consumed { get; private set; }

    // Which kind of reader counted as the target reading the value.
    public Reader? ConsumedBy { get; private set; }

    public bool Superseded { get; private set; }

    public int OtherReaders { get; private set; }

    // Completes when the target has read the value or the clipboard was taken over. Continuations never run inside
    // the clipboard message that completed it.
    public Task Settled => _settled.Task;

    public static Reader Classify(uint readerProcessId, PasteTarget target)
    {
        if (readerProcessId == 0) return Reader.Unidentified;
        return readerProcessId == target.WindowProcessId || readerProcessId == target.FocusProcessId ? Reader.Target : Reader.Other;
    }

    public Reader OnRendered(uint readerProcessId)
    {
        var reader = Classify(readerProcessId, Target);
        if (reader == Reader.Other)
        {
            OtherReaders++;
        }
        else if (!Consumed)
        {
            Consumed = true;
            ConsumedBy = reader;
            _settled.TrySetResult();
        }
        return reader;
    }

    // Someone else emptied the clipboard: the user (or an app) copied something new.
    public void OnSuperseded()
    {
        Superseded = true;
        _settled.TrySetResult();
    }

    // Restore only while the borrowed clipboard is still ours.
    public bool ShouldRestore(bool stillOwner) => !Superseded && stillOwner;

    public PasteOutcome Outcome(RestoreResult restore) =>
        Superseded || restore == RestoreResult.LeftAlone ? PasteOutcome.Superseded
        : Consumed ? PasteOutcome.Consumed
        : PasteOutcome.NotConsumed;
}

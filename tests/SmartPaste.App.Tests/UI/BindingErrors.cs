using System.Diagnostics;
using System.Text;

// Collects WPF data binding errors ("BindingExpression path error" and friends) while it is alive. WPF reports them
// only through a trace source that is off unless a debugger is attached; Refresh turns WPF tracing on.
internal sealed class BindingErrors : TraceListener
{
    private readonly StringBuilder _errors = new();
    private readonly SourceLevels _previousLevel;

    public BindingErrors()
    {
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        _previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(this);
    }

    public string Text => _errors.ToString();

    public override void Write(string? message) => _errors.Append(message);

    public override void WriteLine(string? message) => _errors.AppendLine(message);

    protected override void Dispose(bool disposing)
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Remove(this);
        source.Switch.Level = _previousLevel;
        base.Dispose(disposing);
    }
}

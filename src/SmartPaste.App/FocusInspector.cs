using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SmartPaste.App.Native;
using SmartPaste.Core;
using Interop.UIAutomationClient;

namespace SmartPaste.App;
// Typed COM interfaces only: IUIAutomation is IUnknown-based, so `dynamic` (IDispatch) binding fails at runtime.
internal sealed class FocusInspector : IDisposable
{
    private readonly BlockingCollection<Action> _queue=new();
    private readonly Thread _thread;
    private const int ComboBoxId=50003, EditId=50004, TextId=50020; // UIA_*ControlTypeId
    private IUIAutomation? _automation;
    internal Rectangle? LastBounds { get; private set; }
    internal FocusInspector()
    {
        _thread=new Thread(Run) { IsBackground=true,Name="SmartPaste UIA" };
        _thread.SetApartmentState(ApartmentState.MTA); _thread.Start();
    }
    private void Run() { foreach(var action in _queue.GetConsumingEnumerable()) action(); }
    internal async Task<FieldContext?> InspectAsync(TimeSpan budget)
    {
        var tcs=new TaskCompletionSource<FieldContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() => { try { tcs.TrySetResult(Inspect()); } catch { tcs.TrySetResult(null); } });
        try { return await tcs.Task.WaitAsync(budget); } catch(TimeoutException) { return null; }
    }
    private FieldContext? Inspect()
    {
        LastBounds=null;
        _automation ??= new CUIAutomation8();
        IUIAutomationElement? element=_automation.GetFocusedElement();
        if(element is null) return null;
        int pid=element.CurrentProcessId;
        if(pid==Environment.ProcessId || Win32.ProcessName((uint)pid) is not { } process || InterceptGate.Blocked.Contains(process)) return null;
        // Only single-value inputs qualify. Documents (Word, rich editors) and everything else get a plain paste.
        string? type=element.CurrentControlType switch { EditId => "Edit", ComboBoxId => "ComboBox", _ => null };
        // A native ComboBox's focused element is its inner Edit; report the ComboBox so formatting sees it.
        if(type=="Edit") try { if(_automation.ControlViewWalker.GetParentElement(element)?.CurrentControlType==ComboBoxId) type="ComboBox"; } catch(COMException) { }
        if(type is null) return null;
        bool password=element.CurrentIsPassword!=0;
        string? name=Read(() => element.CurrentName), help=Read(() => element.CurrentHelpText);
        string? id=Read(() => element.CurrentAutomationId);
        string? labeled=Read(() => element.CurrentLabeledBy?.CurrentName);
        // Nearby label text: up to 3 previous Text siblings, farthest first, so the nearest is last
        // (FieldContext.BestLabel uses the last one). Group/parent names are not labels; leaving them out
        // lets the OCR fallback run when a field truly has no label.
        var near=new List<string>();
        try
        {
            IUIAutomationTreeWalker walker=_automation.ControlViewWalker;
            var sibling=walker.GetPreviousSiblingElement(element);
            for(int i=0;i<3 && sibling is not null;i++)
            {
                var current=sibling;
                if(current.CurrentControlType==TextId && Read(() => current.CurrentName) is { Length: >0 } n) near.Add(n);
                sibling=walker.GetPreviousSiblingElement(current);
            }
            near.Reverse();
        }
        catch(COMException) { }
        try { var r=element.CurrentBoundingRectangle; LastBounds=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom); }
        catch(COMException) { LastBounds=null; }
        return new FieldContext(name,help,id,labeled,type,password,near);
    }
    private static string? Read(Func<string?> get) { try { var v=get(); return string.IsNullOrWhiteSpace(v)?null:v.Trim(); } catch(COMException) { return null; } }
    public void Dispose() { _queue.CompleteAdding(); }
}

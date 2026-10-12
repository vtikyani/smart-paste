using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Interop.UIAutomationClient;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.Core;

namespace SmartPaste.App.Shell;

// The focused field as UI Automation describes it. Bounds are physical screen pixels.
internal sealed record FocusedField(FieldContext Context, ScreenRect Bounds, int[] RuntimeId, uint ProcessId, IntPtr NativeWindowHandle);

// Reads the focused element through UI Automation and describes it as a FieldContext. Every UIA call runs on one
// dedicated MTA thread with UIA's connection and transaction timeouts set, so a hung app can stall neither the caller
// nor the rest of Smart Paste. Properties come back in batches through cache requests: each round trip into another
// process costs around half a millisecond (Chromium), so the number of calls decides the speed.
//
// Threads: every public member may be called from any thread. FocusChanged is raised on the dispatcher's thread.
internal sealed class FocusInspector : IDisposable
{
    private const uint UiaTimeoutMs = 1000;
    private const long FocusEventBudgetMs = 800;
    private const int ChromiumWarmUpMs = 150;
    private const int MaxSiblingSteps = 25;
    private const int MaxAncestorLevels = 8;
    private const int MaxHeadingSteps = 15;
    private const uint EditLimitTimeoutMs = 200;

    private static readonly int[] FieldProperties =
    [
        UIA_PropertyIds.UIA_ControlTypePropertyId,
        UIA_PropertyIds.UIA_NamePropertyId,
        UIA_PropertyIds.UIA_HelpTextPropertyId,
        UIA_PropertyIds.UIA_AutomationIdPropertyId,
        UIA_PropertyIds.UIA_LabeledByPropertyId,
        UIA_PropertyIds.UIA_IsPasswordPropertyId,
        UIA_PropertyIds.UIA_ProcessIdPropertyId,
        UIA_PropertyIds.UIA_BoundingRectanglePropertyId,
        UIA_PropertyIds.UIA_NativeWindowHandlePropertyId,
        UIA_PropertyIds.UIA_RuntimeIdPropertyId,
        UIA_PropertyIds.UIA_IsValuePatternAvailablePropertyId,
        UIA_PropertyIds.UIA_ValueIsReadOnlyPropertyId,
        UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId,
        UIA_PropertyIds.UIA_FrameworkIdPropertyId,
        UIA_PropertyIds.UIA_FullDescriptionPropertyId,
        UIA_PropertyIds.UIA_AriaPropertiesPropertyId,
        UIA_PropertyIds.UIA_ClassNamePropertyId
    ];

    private static readonly int[] NodeProperties =
    [
        UIA_PropertyIds.UIA_ControlTypePropertyId,
        UIA_PropertyIds.UIA_NamePropertyId,
        UIA_PropertyIds.UIA_HeadingLevelPropertyId,
        UIA_PropertyIds.UIA_LandmarkTypePropertyId,
        UIA_PropertyIds.UIA_IsDialogPropertyId,
        UIA_PropertyIds.UIA_BoundingRectanglePropertyId
    ];

    private readonly Dispatcher _dispatcher;
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    // UIA thread only.
    private IUIAutomation? _automation;
    private IUIAutomationCacheRequest? _fieldCache;
    private IUIAutomationCacheRequest? _nodeCache;
    private IUIAutomationTreeWalker? _walker;
    private FocusHandler? _focusHandler;
    private int[]? _lastReported;
    private bool _lastReportedNone;

    // Written on UIA's event thread, taken on ours.
    private IUIAutomationElement? _pendingFocus;
    private int _focusQueued;

    public FocusInspector(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "Smart Paste UI Automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    // While watching: the newly focused field, or null when focus moved to something that is not an editable field.
    public event Action<FocusedField?>? FocusChanged;

    private static long Now => Environment.TickCount64;

    // The focused element when it is an editable single-value field (a text box or an editable combo box); null for
    // anything else, for our own windows, or when the budget runs out.
    public async Task<FocusedField?> InspectAsync(TimeSpan budget, CancellationToken ct)
    {
        var result = new TaskCompletionSource<FocusedField?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = Now + (long)budget.TotalMilliseconds;
        var queued = Enqueue(() =>
        {
            // The caller may have given up while this waited in line.
            if (result.Task.IsCompleted) return;
            try
            {
                result.TrySetResult(InspectFocused(deadline));
            }
            catch (Exception ex) when (IsUiaFailure(ex))
            {
                Log.Error("uia_inspect_error", ex);
                result.TrySetResult(null);
            }
        });
        if (!queued) return null;

        try
        {
            return await result.Task.WaitAsync(budget, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            result.TrySetResult(null);
            Log.Event("uia_inspect_timeout", ("ms", (long)budget.TotalMilliseconds));
            return null;
        }
        catch (OperationCanceledException)
        {
            result.TrySetCanceled(ct);
            throw;
        }
    }

    public void StartWatchingFocus() => Enqueue(() =>
    {
        if (_focusHandler is not null) return;
        var automation = Automation();
        var handler = new FocusHandler(this);
        automation.AddFocusChangedEventHandler(_fieldCache, handler);
        _focusHandler = handler;
        _lastReported = null;
        _lastReportedNone = false;
    });

    public void StopWatchingFocus() => Enqueue(() =>
    {
        if (_focusHandler is not { } handler) return;
        _focusHandler = null;
        Interlocked.Exchange(ref _pendingFocus, null);
        try
        {
            _automation?.RemoveFocusChangedEventHandler(handler);
        }
        catch (Exception ex) when (IsUiaFailure(ex))
        {
            Log.Error("uia_unwatch_error", ex);
        }
    });

    public void Dispose()
    {
        StopWatchingFocus();
        _work.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(2))) _work.Dispose();
    }

    private static bool IsUiaFailure(Exception ex) => ex is COMException or TimeoutException or InvalidOperationException
        or InvalidCastException or ArgumentException or UnauthorizedAccessException;

    private bool Enqueue(Action work)
    {
        try
        {
            _work.Add(work);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Disposed: no more work is accepted.
            return false;
        }
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // Every later inspection depends on this thread; one failed call must not end it.
                Log.Error("uia_thread_error", ex);
            }
        }
    }

    private IUIAutomation Automation()
    {
        if (_automation is not null) return _automation;
        var automation = new CUIAutomation8();
        // Without these a hung app can hold a call for 20 seconds.
        var tuned = (IUIAutomation2)automation;
        tuned.ConnectionTimeout = UiaTimeoutMs;
        tuned.TransactionTimeout = UiaTimeoutMs;
        _fieldCache = automation.CreateCacheRequest();
        foreach (var property in FieldProperties) _fieldCache.AddProperty(property);
        _nodeCache = automation.CreateCacheRequest();
        foreach (var property in NodeProperties) _nodeCache.AddProperty(property);
        _walker = automation.ControlViewWalker;
        _automation = automation;
        return automation;
    }

    private FocusedField? InspectFocused(long deadline)
    {
        var automation = Automation();
        var element = automation.GetFocusedElementBuildCache(_fieldCache);
        if (element is null) return null;
        var field = Describe(element, deadline);
        if (field is not null || !IsWarmingUp(element) || Now + ChromiumWarmUpMs >= deadline) return field;

        // Chromium builds its accessibility tree on the first UI Automation contact; until then focus sits on a busy
        // document. Give it one moment.
        Thread.Sleep(ChromiumWarmUpMs);
        element = automation.GetFocusedElementBuildCache(_fieldCache);
        return element is null ? null : Describe(element, deadline);
    }

    private static bool IsWarmingUp(IUIAutomationElement element) =>
        string.Equals(Text(element, UIA_PropertyIds.UIA_ClassNamePropertyId), "Chrome_RenderWidgetHostHWND", StringComparison.Ordinal) ||
        (Int(element, UIA_PropertyIds.UIA_ControlTypePropertyId) == UiaControlTypes.Document &&
         (Text(element, UIA_PropertyIds.UIA_AriaPropertiesPropertyId)?.Contains("busy=true", StringComparison.OrdinalIgnoreCase) ?? false));

    private FocusedField? Describe(IUIAutomationElement element, long deadline)
    {
        var processId = (uint)Int(element, UIA_PropertyIds.UIA_ProcessIdPropertyId);
        if (processId == 0 || processId == _ownProcessId) return null;
        var controlType = Int(element, UIA_PropertyIds.UIA_ControlTypePropertyId);
        var qualifies = FieldRules.Qualifies(
            controlType,
            Bool(element, UIA_PropertyIds.UIA_IsValuePatternAvailablePropertyId),
            Bool(element, UIA_PropertyIds.UIA_ValueIsReadOnlyPropertyId),
            Bool(element, UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId));
        if (!qualifies) return null;

        var parent = Parent(element);
        var anchor = element;
        if (controlType == UiaControlTypes.Edit && parent is not null && Int(parent, UIA_PropertyIds.UIA_ControlTypePropertyId) == UiaControlTypes.ComboBox)
        {
            // A native editable combo box: its inner edit has the focus, but label and neighbors belong to the combo.
            anchor = parent.BuildUpdatedCache(_fieldCache);
            controlType = UiaControlTypes.ComboBox;
            parent = Parent(anchor);
        }

        var parentNode = parent is null ? (UiaNode?)null : Node(parent);
        var anchorRect = Bounds(anchor);
        var groupCaption = parentNode is { ControlType: UiaControlTypes.Group } group ? group.Name : null;
        var siblings = new SiblingWalk(this, anchor, MaxSiblingSteps, deadline);
        var scan = FieldRules.ScanPreviousSiblings(siblings, groupCaption, anchorRect);

        var helpText = Text(element, UIA_PropertyIds.UIA_HelpTextPropertyId) ?? Text(anchor, UIA_PropertyIds.UIA_HelpTextPropertyId);
        var description = Text(element, UIA_PropertyIds.UIA_FullDescriptionPropertyId);
        var sections = new SectionsBuilder();
        sections.AddContext(scan.Context);
        // aria-describedby text: help when there is no other, context when there is.
        if (helpText is not null && description is not null && description != helpText) sections.AddContext(description);
        sections.AddHeading(scan.Heading);
        sections.AddGroup(scan.EnclosingGroup);
        // A heading further up belongs to this field only if nothing closer could have been missed: when the walk
        // stopped early, a heading above might be the previous section's.
        CollectSections(parent, sections, headingSearchDone: scan.Heading is not null || !siblings.ReachedFirst, deadline);

        var isCombo = !ReferenceEquals(anchor, element);
        var context = new FieldContext(
            Name: Text(element, UIA_PropertyIds.UIA_NamePropertyId) ?? (isCombo ? Text(anchor, UIA_PropertyIds.UIA_NamePropertyId) : null),
            HelpText: helpText ?? description,
            AutomationId: Text(element, UIA_PropertyIds.UIA_AutomationIdPropertyId) ?? (isCombo ? Text(anchor, UIA_PropertyIds.UIA_AutomationIdPropertyId) : null),
            LabeledBy: LabeledBy(element) ?? (isCombo ? LabeledBy(anchor) : null),
            ControlType: FieldRules.ControlTypeName(controlType),
            IsPassword: Bool(element, UIA_PropertyIds.UIA_IsPasswordPropertyId),
            Siblings: scan.NearbyText,
            MaxLength: MaxLength(element))
        {
            Placeholder = FieldRules.Placeholder(Text(element, UIA_PropertyIds.UIA_FrameworkIdPropertyId), helpText, description),
            Sections = sections.Build(),
            AppName = ProcessInfo.TryGetName(processId)
        };
        var runtimeId = element.GetCachedPropertyValue(UIA_PropertyIds.UIA_RuntimeIdPropertyId) as int[] ?? [];
        var handle = new IntPtr(Int(element, UIA_PropertyIds.UIA_NativeWindowHandlePropertyId));
        return new FocusedField(context, anchorRect, runtimeId, processId, handle);
    }

    // Containers above the field, nearest first, and the nearest heading before it at any of those levels.
    private void CollectSections(IUIAutomationElement? level, SectionsBuilder sections, bool headingSearchDone, long deadline)
    {
        for (var depth = 0; level is not null && depth < MaxAncestorLevels && Now < deadline; depth++)
        {
            var node = Node(level);
            if (FieldRules.EndsSearch(node))
            {
                if (node.IsDialog) sections.AddGroup(node.Name);
                return;
            }
            if (FieldRules.IsSection(node)) sections.AddGroup(node.Name);
            if (!headingSearchDone)
            {
                var siblings = new SiblingWalk(this, level, MaxHeadingSteps, deadline);
                var heading = FieldRules.FindHeading(siblings);
                sections.AddHeading(heading);
                headingSearchDone = heading is not null || !siblings.ReachedFirst;
            }
            if (headingSearchDone && sections.HasAllGroups) return;
            level = Parent(level);
        }
    }

    // The previous sibling, or null at the first child. Failures throw to the caller.
    private IUIAutomationElement? PreviousSibling(IUIAutomationElement element) =>
        _walker!.GetPreviousSiblingElementBuildCache(element, _nodeCache);

    private IUIAutomationElement? Parent(IUIAutomationElement element)
    {
        try
        {
            return _walker!.GetParentElementBuildCache(element, _nodeCache);
        }
        catch (Exception ex) when (IsUiaFailure(ex))
        {
            return null;
        }
    }

    private static UiaNode Node(IUIAutomationElement element) => new(
        Int(element, UIA_PropertyIds.UIA_ControlTypePropertyId),
        Text(element, UIA_PropertyIds.UIA_NamePropertyId),
        FieldRules.HeadingLevel(Int(element, UIA_PropertyIds.UIA_HeadingLevelPropertyId)),
        Int(element, UIA_PropertyIds.UIA_LandmarkTypePropertyId),
        Bool(element, UIA_PropertyIds.UIA_IsDialogPropertyId),
        Bounds(element));

    private static ScreenRect Bounds(IUIAutomationElement element)
    {
        var rect = element.CachedBoundingRectangle;
        return ScreenRect.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
    }

    // The label element's name costs one more round trip, and only when there is a label element.
    private static string? LabeledBy(IUIAutomationElement element)
    {
        try
        {
            return element.CachedLabeledBy is { } label ? FieldRules.Clean(label.CurrentName) : null;
        }
        catch (Exception ex) when (IsUiaFailure(ex))
        {
            return null;
        }
    }

    // EM_GETLIMITTEXT for native edit boxes. Web fields do not expose maxlength through UI Automation.
    private static int? MaxLength(IUIAutomationElement element)
    {
        var handle = new IntPtr(Int(element, UIA_PropertyIds.UIA_NativeWindowHandlePropertyId));
        if (handle == IntPtr.Zero || !FieldRules.IsNativeEditClass(WindowClass(handle))) return null;
        var answered = NativeMethods.SendMessageTimeout(handle, NativeMethods.EmGetLimitText, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.SmtoAbortIfHung | NativeMethods.SmtoErrorOnExit, EditLimitTimeoutMs, out var limit);
        return answered == IntPtr.Zero ? null : FieldRules.NormalizeTextLimit(limit.ToInt64());
    }

    private static string? WindowClass(IntPtr window)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? Text(IUIAutomationElement element, int property) =>
        FieldRules.Clean(element.GetCachedPropertyValue(property) as string);

    private static int Int(IUIAutomationElement element, int property) =>
        element.GetCachedPropertyValue(property) is int value ? value : 0;

    private static bool Bool(IUIAutomationElement element, int property) =>
        element.GetCachedPropertyValue(property) is bool value && value;

    // UIA's event thread: remember the element and wake ours. Never block here.
    private void OnFocusEvent(IUIAutomationElement sender)
    {
        Interlocked.Exchange(ref _pendingFocus, sender);
        if (Interlocked.Exchange(ref _focusQueued, 1) == 0) Enqueue(ProcessFocus);
    }

    // Only the latest focus counts: quick tabbing through a form describes just the field it stops on.
    private void ProcessFocus()
    {
        Volatile.Write(ref _focusQueued, 0);
        var element = Interlocked.Exchange(ref _pendingFocus, null);
        if (element is null || _focusHandler is null) return;

        FocusedField? field;
        try
        {
            field = Describe(element, Now + FocusEventBudgetMs);
        }
        catch (Exception ex) when (IsUiaFailure(ex))
        {
            field = null;
        }

        if (field is null)
        {
            if (_lastReportedNone) return;
            _lastReportedNone = true;
            _lastReported = null;
        }
        else
        {
            if (field.RuntimeId.Length > 0 && _lastReported is not null && _lastReported.AsSpan().SequenceEqual(field.RuntimeId)) return;
            _lastReported = field.RuntimeId;
            _lastReportedNone = false;
        }
        _dispatcher.BeginInvoke(() => FocusChanged?.Invoke(field));
    }

    private sealed class FocusHandler(FocusInspector owner) : IUIAutomationFocusChangedEventHandler
    {
        public void HandleFocusChangedEvent(IUIAutomationElement sender) => owner.OnFocusEvent(sender);
    }

    // The elements before one element, nearest first, fetched one round trip at a time and only as far as the reader
    // goes. ReachedFirst tells a walk that saw every earlier sibling from one cut short by its step limit, the
    // deadline, or a failure.
    private sealed class SiblingWalk(FocusInspector owner, IUIAutomationElement start, int maxSteps, long deadline) : IEnumerable<UiaNode>
    {
        public bool ReachedFirst { get; private set; }

        public IEnumerator<UiaNode> GetEnumerator()
        {
            var current = start;
            for (var step = 0; step < maxSteps && Now < deadline; step++)
            {
                var sibling = Next(current, out var failed);
                if (failed) yield break;
                if (sibling is null)
                {
                    ReachedFirst = true;
                    yield break;
                }
                yield return Node(sibling);
                current = sibling;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IUIAutomationElement? Next(IUIAutomationElement element, out bool failed)
        {
            try
            {
                failed = false;
                return owner.PreviousSibling(element);
            }
            catch (Exception ex) when (IsUiaFailure(ex))
            {
                failed = true;
                return null;
            }
        }
    }
}

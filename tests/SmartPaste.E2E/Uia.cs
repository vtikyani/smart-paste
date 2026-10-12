using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace SmartPaste.E2E;

// Just enough UI Automation to find fields, focus them, and read what landed in them.
internal sealed class Uia
{
    private const int AutomationIdProperty = 30011, NameProperty = 30005, ControlTypeProperty = 30003;
    private const int ValuePatternId = 10002, WindowControlType = 50032;

    private readonly CUIAutomation8 _automation = new();

    public IUIAutomationElement Element(IntPtr hwnd) => _automation.ElementFromHandle(hwnd);

    public IUIAutomationElement? FindById(IUIAutomationElement root, string automationId, TimeSpan timeout)
    {
        var condition = _automation.CreatePropertyCondition(AutomationIdProperty, automationId);
        return Poll(() => root.FindFirst(TreeScope.TreeScope_Descendants, condition), timeout);
    }

    // A top-level window whose title starts with the given text (browser titles get a suffix).
    public IUIAutomationElement? FindWindow(string titlePrefix, TimeSpan timeout)
    {
        var windows = _automation.CreatePropertyCondition(ControlTypeProperty, WindowControlType);
        return Poll(() =>
        {
            var all = _automation.GetRootElement().FindAll(TreeScope.TreeScope_Children, windows);
            for (var i = 0; i < all.Length; i++)
            {
                var name = Safe(() => all.GetElement(i).CurrentName);
                if (name?.StartsWith(titlePrefix, StringComparison.Ordinal) == true) return all.GetElement(i);
            }
            return null;
        }, timeout);
    }

    public string? Value(IUIAutomationElement element) => Safe(() =>
        element.GetCurrentPattern(ValuePatternId) is IUIAutomationValuePattern pattern ? pattern.CurrentValue : null);

    public void SetValue(IUIAutomationElement element, string value) => Safe(() =>
    {
        if (element.GetCurrentPattern(ValuePatternId) is IUIAutomationValuePattern pattern) pattern.SetValue(value);
        return true;
    });

    public bool Focus(IUIAutomationElement element, TimeSpan timeout)
    {
        Safe(() =>
        {
            element.SetFocus();
            return true;
        });
        return Poll(() => HasFocus(element) ? (object)true : null, timeout) is not null;
    }

    public bool HasFocus(IUIAutomationElement element) => Safe(() =>
    {
        var focused = _automation.GetFocusedElement();
        return focused is not null && _automation.CompareElements(focused, element) != 0;
    });

    public string? Name(IUIAutomationElement element) => Safe(() => element.CurrentName);

    private static T? Poll<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var result = Safe(probe);
            if (result is not null || DateTime.UtcNow > until) return result;
            Thread.Sleep(100);
        }
    }

    private static T? Safe<T>(Func<T?> call)
    {
        try { return call(); }
        catch (COMException) { return default; }
        catch (InvalidCastException) { return default; }
    }
}

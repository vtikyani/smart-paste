using System.Text.RegularExpressions;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// UI Automation control type ids (UIA_*ControlTypeId) used by the field rules.
internal static class UiaControlTypes
{
    public const int Button = 50000;
    public const int Calendar = 50001;
    public const int CheckBox = 50002;
    public const int ComboBox = 50003;
    public const int Edit = 50004;
    public const int Hyperlink = 50005;
    public const int ListItem = 50007;
    public const int List = 50008;
    public const int Menu = 50009;
    public const int MenuBar = 50010;
    public const int MenuItem = 50011;
    public const int RadioButton = 50013;
    public const int ScrollBar = 50014;
    public const int Slider = 50015;
    public const int Spinner = 50016;
    public const int Tab = 50018;
    public const int TabItem = 50019;
    public const int Text = 50020;
    public const int ToolBar = 50021;
    public const int Tree = 50023;
    public const int TreeItem = 50024;
    public const int Group = 50026;
    public const int DataGrid = 50028;
    public const int DataItem = 50029;
    public const int Document = 50030;
    public const int SplitButton = 50031;
    public const int Window = 50032;
    public const int Pane = 50033;
    public const int Header = 50034;
    public const int HeaderItem = 50035;
    public const int Table = 50036;
}

// A UI Automation element reduced to what the rules look at. HeadingLevel is 1 to 9 for headings, 0 otherwise.
internal readonly record struct UiaNode(
    int ControlType, string? Name, int HeadingLevel = 0, int LandmarkType = 0, bool IsDialog = false, ScreenRect Bounds = default);

// What the elements before a field say about it. NearbyText holds possible labels, nearest last.
internal sealed record SiblingScan(IReadOnlyList<string> NearbyText, string? Context, string? Heading, string? EnclosingGroup);

// The pure rules behind FocusInspector: which elements are fields, which nearby text can be a label, and which
// containers name the section a field sits in.
internal static partial class FieldRules
{
    public const int MaxNearbyText = 3;

    // Longer text near a field is an instruction, not a label; it becomes section context instead.
    public const int MaxLabelLength = 80;

    public const int MaxContextLength = 120;

    public const int MaxSectionGroups = 4;

    // Single-value text inputs only. A combo box also needs an editable text part: Chromium reports an HTML <select>
    // as a combo box whose value is not read-only, and only the text pattern tells it apart from an editable one.
    public static bool Qualifies(int controlType, bool hasValuePattern, bool isReadOnly, bool hasTextPattern) => controlType switch
    {
        UiaControlTypes.Edit => hasValuePattern && !isReadOnly,
        UiaControlTypes.ComboBox => hasValuePattern && !isReadOnly && hasTextPattern,
        _ => false
    };

    public static string? ControlTypeName(int controlType) => controlType switch
    {
        UiaControlTypes.Edit => "Edit",
        UiaControlTypes.ComboBox => "ComboBox",
        _ => null
    };

    // Another control between a field and some text means that text belongs to that control, not to this field.
    public static bool IsInputLike(int controlType) => controlType is UiaControlTypes.Button or UiaControlTypes.Calendar
        or UiaControlTypes.CheckBox or UiaControlTypes.ComboBox or UiaControlTypes.Edit or UiaControlTypes.List
        or UiaControlTypes.ListItem or UiaControlTypes.Menu or UiaControlTypes.MenuBar or UiaControlTypes.MenuItem
        or UiaControlTypes.RadioButton or UiaControlTypes.ScrollBar or UiaControlTypes.Slider or UiaControlTypes.Spinner
        or UiaControlTypes.SplitButton or UiaControlTypes.Tab or UiaControlTypes.TabItem or UiaControlTypes.ToolBar
        or UiaControlTypes.Tree or UiaControlTypes.TreeItem or UiaControlTypes.DataGrid or UiaControlTypes.DataItem
        or UiaControlTypes.Table or UiaControlTypes.Header or UiaControlTypes.HeaderItem or UiaControlTypes.Document
        or UiaControlTypes.Group or UiaControlTypes.Pane;

    // Walks the elements before a field, nearest first. Text counts as a possible label only until the walk reaches
    // another control: past it, text labels that control (an unlabeled field must not borrow "Card number" from the
    // row above). The fieldset's own caption is a section, not a label. The nearest heading is looked for further.
    public static SiblingScan ScanPreviousSiblings(IEnumerable<UiaNode> nearestFirst, string? groupCaption, ScreenRect fieldBounds = default)
    {
        var labels = new List<string>(MaxNearbyText);
        string? context = null;
        string? heading = null;
        string? enclosingGroup = null;
        var collecting = true;

        foreach (var node in nearestFirst)
        {
            if (node.HeadingLevel > 0)
            {
                heading = Clean(node.Name);
                if (heading is null) continue;
                // A heading right above the field, with nothing in between, is the only label it has.
                if (collecting && labels.Count == 0) labels.Add(heading);
                break;
            }

            if (IsInputLike(node.ControlType))
            {
                collecting = false;
                // A group box drawn around the field (native dialogs) is a sibling, not an ancestor.
                if (enclosingGroup is null && node.ControlType == UiaControlTypes.Group && Encloses(node.Bounds, fieldBounds))
                    enclosingGroup = Clean(node.Name);
                continue;
            }

            if (!collecting || node.ControlType != UiaControlTypes.Text) continue;
            var text = Clean(node.Name);
            if (text is null || string.Equals(text, Clean(groupCaption), StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length > MaxLabelLength)
            {
                context ??= Cut(text, MaxContextLength);
                continue;
            }
            if (labels.Count < MaxNearbyText) labels.Add(text);
        }

        labels.Reverse();
        return new SiblingScan(labels, context, heading, enclosingGroup);
    }

    // The nearest heading among elements given nearest first.
    public static string? FindHeading(IEnumerable<UiaNode> nearestFirst)
    {
        foreach (var node in nearestFirst)
            if (node.HeadingLevel > 0 && Clean(node.Name) is { } heading) return heading;
        return null;
    }

    // Containers whose name says which part of a form a field is in: fieldsets and groups, forms and other
    // landmarks, dialogs.
    public static bool IsSection(in UiaNode node) =>
        Clean(node.Name) is not null &&
        (node.ControlType == UiaControlTypes.Group || node.LandmarkType != 0 || node.IsDialog);

    // The walk up stops at a web page's document or an app's window. A dialog window still names a section.
    public static bool EndsSearch(in UiaNode node) =>
        node.ControlType is UiaControlTypes.Document or UiaControlTypes.Window;

    // UIA_HeadingLevelPropertyId values (HeadingLevel1..9 = 80051..80059) as 1 to 9; anything else is 0.
    public static int HeadingLevel(int uiaValue) => uiaValue is >= 80051 and <= 80059 ? uiaValue - 80050 : 0;

    // Chromium exposes an input's placeholder as its help text (or the title attribute when there is no placeholder),
    // with aria-describedby separately as the full description. Other frameworks have no reliable mapping.
    public static string? Placeholder(string? frameworkId, string? helpText, string? fullDescription)
    {
        if (!string.Equals(frameworkId, "Chrome", StringComparison.OrdinalIgnoreCase)) return null;
        var help = Clean(helpText);
        return help is null || string.Equals(help, Clean(fullDescription), StringComparison.Ordinal) ? null : help;
    }

    // EM_GETLIMITTEXT answers 30000 or 32767 for an edit box nobody limited; only a smaller limit is a real rule.
    public static int? NormalizeTextLimit(long limit) => limit is > 0 and < 30000 ? (int)limit : null;

    // Win32 edit and rich edit classes, including the WinForms wrappers ("WindowsForms10.EDIT.app.0...").
    public static bool IsNativeEditClass(string? className) =>
        className is not null &&
        (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
         className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
         className.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase) ||
         className.Contains(".RichEdit", StringComparison.OrdinalIgnoreCase));

    // Trimmed, with runs of whitespace collapsed; null when nothing is left.
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Whitespace().Replace(text.Trim(), " ");
    }

    // Shortened at a word boundary.
    public static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var space = text.LastIndexOf(' ', maxLength);
        return (space > maxLength / 2 ? text[..space] : text[..maxLength]).TrimEnd();
    }

    private static bool Encloses(ScreenRect outer, ScreenRect inner)
    {
        if (outer.IsEmpty || inner.IsEmpty) return false;
        var x = inner.Left + inner.Width / 2;
        var y = inner.Top + inner.Height / 2;
        return x >= outer.Left && x < outer.Right && y >= outer.Top && y < outer.Bottom;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}

// Section names for a field, nearest first, without duplicates: at most four containers plus headings and context.
internal sealed class SectionsBuilder
{
    private readonly List<string> _items = [];
    private int _groups;

    public bool HasAllGroups => _groups >= FieldRules.MaxSectionGroups;

    public void AddContext(string? text)
    {
        if (FieldRules.Clean(text) is { } clean) Add(FieldRules.Cut(clean, FieldRules.MaxContextLength));
    }

    public void AddHeading(string? text) => Add(text);

    public void AddGroup(string? name)
    {
        if (!HasAllGroups && Add(name)) _groups++;
    }

    public IReadOnlyList<string> Build() => _items.ToArray();

    private bool Add(string? text)
    {
        var clean = FieldRules.Clean(text);
        if (clean is null || _items.Contains(clean, StringComparer.OrdinalIgnoreCase)) return false;
        _items.Add(clean);
        return true;
    }
}

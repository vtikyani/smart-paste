using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Tray;

// Where the tray flyout goes, like Windows 11's own tray flyouts: beside the taskbar (above a bottom taskbar),
// centered on the tray icon, and kept inside the monitor's work area with a margin. Physical pixels throughout.
internal static class FlyoutPlacement
{
    public static (int X, int Y) Place(int width, int height, ScreenRect anchor, ScreenRect workArea, TaskbarPosition? taskbar, int margin)
    {
        var area = Usable(workArea, taskbar);
        var edge = taskbar?.Edge ?? TaskbarEdge.Bottom;
        var centerX = anchor.Left + anchor.Width / 2;
        var centerY = anchor.Top + anchor.Height / 2;

        var (x, y) = edge switch
        {
            TaskbarEdge.Top => (centerX - width / 2, area.Top + margin),
            TaskbarEdge.Left => (area.Left + margin, centerY - height / 2),
            TaskbarEdge.Right => (area.Right - margin - width, centerY - height / 2),
            _ => (centerX - width / 2, area.Bottom - margin - height)
        };
        return (Clamp(x, area.Left + margin, area.Right - margin - width),
                Clamp(y, area.Top + margin, area.Bottom - margin - height));
    }

    // The tallest the flyout may be on this monitor.
    public static int MaxHeight(ScreenRect workArea, TaskbarPosition? taskbar, int margin) =>
        Math.Max(0, Usable(workArea, taskbar).Height - 2 * margin);

    // The work area, minus the taskbar when it overlaps it (an auto-hidden taskbar is not part of the work area
    // but is on screen while the user clicks its icons).
    private static ScreenRect Usable(ScreenRect workArea, TaskbarPosition? taskbar)
    {
        if (taskbar is not { } bar || !Overlaps(workArea, bar.Bounds)) return workArea;
        return bar.Edge switch
        {
            TaskbarEdge.Top => ScreenRect.FromLTRB(workArea.Left, Math.Max(workArea.Top, bar.Bounds.Bottom), workArea.Right, workArea.Bottom),
            TaskbarEdge.Left => ScreenRect.FromLTRB(Math.Max(workArea.Left, bar.Bounds.Right), workArea.Top, workArea.Right, workArea.Bottom),
            TaskbarEdge.Right => ScreenRect.FromLTRB(workArea.Left, workArea.Top, Math.Min(workArea.Right, bar.Bounds.Left), workArea.Bottom),
            _ => ScreenRect.FromLTRB(workArea.Left, workArea.Top, workArea.Right, Math.Min(workArea.Bottom, bar.Bounds.Top))
        };
    }

    private static bool Overlaps(ScreenRect a, ScreenRect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    // Prefers the low bound when the flyout is larger than the space, so its top-left stays visible.
    private static int Clamp(int value, int low, int high) => Math.Max(low, Math.Min(value, high));
}

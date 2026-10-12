using System.Windows;

namespace SmartPaste.App.UI.Navigation;

internal static class WindowSizing
{
    private const double Gap = 24;

    // Keeps a window's opening and minimum size within the screen's usable area (above the taskbar), so a small
    // or highly scaled display never gets a window larger than it can show.
    public static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.Width = Math.Min(window.Width, area.Width - Gap);
        window.Height = Math.Min(window.Height, area.Height - Gap);
        window.MinWidth = Math.Min(window.MinWidth, window.Width);
        window.MinHeight = Math.Min(window.MinHeight, window.Height);
    }
}

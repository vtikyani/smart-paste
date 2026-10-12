using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Popups;

// Where a popup's visible surface goes, in physical pixels, relative to the field or caret it belongs to.
internal static class PopupPlacement
{
    // Just below the anchor with left edges aligned; above it when there is no room below; kept inside the
    // work area either way.
    public static (int X, int Y) BelowOrAbove(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap)
    {
        var below = anchor.Bottom + gap;
        var above = anchor.Top - gap - height;
        int y;
        if (below + height <= workArea.Bottom) y = below;
        else if (above >= workArea.Top) y = above;
        // Neither side has room: use the roomier side and let the clamp keep it on screen.
        else y = workArea.Bottom - anchor.Bottom >= anchor.Top - workArea.Top ? below : above;
        return (Clamp(anchor.Left, workArea.Left, workArea.Right - width), Clamp(y, workArea.Top, workArea.Bottom - height));
    }

    // Just above the field's top-right corner, right edges aligned; below the field when there is no room above.
    public static (int X, int Y) FieldHint(ScreenRect field, int width, int height, ScreenRect workArea, int gap)
    {
        var y = field.Top - gap - height;
        if (y < workArea.Top) y = field.Bottom + gap;
        return (Clamp(field.Right - width, workArea.Left, workArea.Right - width), Clamp(y, workArea.Top, workArea.Bottom - height));
    }

    // Prefers the low bound when the popup is larger than the space, so its top-left stays visible.
    private static int Clamp(int value, int low, int high) => Math.Max(low, Math.Min(value, high));
}

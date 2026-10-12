using System.Runtime.InteropServices;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// Where to show things next to the text cursor: the caret when the app reports one inside the field, else the field,
// else the mouse pointer. Physical screen pixels. Call it on the UI thread.
internal static class CaretLocator
{
    public static ScreenRect Locate(IntPtr window, ScreenRect? fieldBounds) =>
        CaretGeometry.Choose(ReadCaret(window), fieldBounds, ReadCursor());

    private static ScreenRect? ReadCaret(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        var thread = NativeMethods.GetWindowThreadProcessId(window, out _);
        if (thread == 0) return null;
        var info = new NativeMethods.GuiThreadInfo { Size = Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        if (!NativeMethods.GetGUIThreadInfo(thread, ref info) || info.Caret == IntPtr.Zero) return null;

        var topLeft = new NativeMethods.Point(info.CaretRect.Left, info.CaretRect.Top);
        var bottomRight = new NativeMethods.Point(info.CaretRect.Right, info.CaretRect.Bottom);
        if (!ToPhysicalScreen(info.Caret, ref topLeft) || !ToPhysicalScreen(info.Caret, ref bottomRight)) return null;
        return CaretGeometry.FromCorners(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    // The caret comes in the client coordinates the app itself works in, which are scaled units for an app that is
    // not per-monitor DPI aware. Map to the screen in the window's own DPI context, then from its units to physical
    // pixels (a no-op for per-monitor aware windows).
    private static bool ToPhysicalScreen(IntPtr window, ref NativeMethods.Point point)
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.GetWindowDpiAwarenessContext(window));
        try
        {
            if (!NativeMethods.ClientToScreen(window, ref point)) return false;
        }
        finally
        {
            if (previous != IntPtr.Zero) NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
        NativeMethods.LogicalToPhysicalPointForPerMonitorDPI(window, ref point);
        return true;
    }

    private static (int X, int Y) ReadCursor() =>
        NativeMethods.GetPhysicalCursorPos(out var point) ? (point.X, point.Y) : (0, 0);
}

// The geometry behind CaretLocator.
internal static class CaretGeometry
{
    // How far outside the field a caret may reach and still count as inside it (borders, padding, line height).
    public const int Tolerance = 4;

    // A caret counts only inside the field: apps leave stale carets behind, and some report one for another control.
    public static ScreenRect Choose(ScreenRect? caret, ScreenRect? field, (int X, int Y) cursor)
    {
        if (field is { IsEmpty: false } bounds)
            return caret is { } inside && IsInside(inside, bounds) ? Normalize(inside) : bounds;
        if (caret is { } only) return Normalize(only);
        return ScreenRect.FromPoint(cursor.X, cursor.Y);
    }

    // The caret's middle lies within the field, allowing for the tolerance.
    public static bool IsInside(ScreenRect caret, ScreenRect field, int tolerance = Tolerance)
    {
        var x = caret.Left + caret.Width / 2;
        var y = caret.Top + caret.Height / 2;
        return x >= field.Left - tolerance && x <= field.Right + tolerance &&
               y >= field.Top - tolerance && y <= field.Bottom + tolerance;
    }

    // Carets are often reported zero pixels wide; anything shown next to one needs at least a 1x1 anchor.
    public static ScreenRect Normalize(ScreenRect caret) =>
        new(caret.Left, caret.Top, Math.Max(1, caret.Width), Math.Max(1, caret.Height));

    public static ScreenRect FromCorners(int left, int top, int right, int bottom) =>
        ScreenRect.FromLTRB(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom));
}

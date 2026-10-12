using System.Runtime.InteropServices;
using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// 32-bit BGRA pixels, opaque, top row first.
internal sealed record CapturedImage(byte[] Bgra, int Width, int Height);

// Reads pixels off the screen with GDI, in physical pixels across all monitors (the app is per-monitor DPI aware).
internal static class ScreenCapture
{
    // The whole desktop across all monitors.
    public static ScreenRect VirtualScreen() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
        NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));

    // The visible frame of the top-level window under the middle of an area, or null.
    public static ScreenRect? WindowBoundsAt(ScreenRect area)
    {
        var window = NativeMethods.WindowFromPoint(new NativeMethods.Point(area.Left + area.Width / 2, area.Top + area.Height / 2));
        if (window == IntPtr.Zero) return null;
        var root = NativeMethods.GetAncestor(window, NativeMethods.GaRoot);
        if (root == IntPtr.Zero) root = window;
        var result = NativeMethods.DwmGetWindowAttribute(
            root, NativeMethods.DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeMethods.Rect>());
        return result == 0 ? ScreenRect.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }

    // Copies a screen region, enlarged by scale with smoothing (small UI text reads better for OCR when enlarged).
    public static CapturedImage? Capture(ScreenRect region, int scale)
    {
        if (region.IsEmpty || scale < 1) return null;
        var width = region.Width * scale;
        var height = region.Height * scale;
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;

        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memory = NativeMethods.CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) return null;
            // A negative height makes the bitmap top-down.
            var header = new NativeMethods.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            bitmap = NativeMethods.CreateDIBSection(memory, ref header, NativeMethods.DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = NativeMethods.SelectObject(memory, bitmap);
            NativeMethods.SetStretchBltMode(memory, NativeMethods.HalftoneStretch);
            NativeMethods.SetBrushOrgEx(memory, 0, 0, IntPtr.Zero);
            if (!NativeMethods.StretchBlt(memory, 0, 0, width, height, screen, region.Left, region.Top, region.Width, region.Height,
                    NativeMethods.SrcCopy | NativeMethods.CaptureBlt))
                return null;

            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves the fourth byte empty.
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;
            return new CapturedImage(pixels, width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero) NativeMethods.SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memory != IntPtr.Zero) NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

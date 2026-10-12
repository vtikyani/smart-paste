using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace SmartPaste.App.UI.Tray;

// A Windows icon (HICON) made from rendered pixels, so the tray shows exactly the pixels we drew rather than a
// copy the shell rescaled. Destroyed when disposed.
internal sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private IconHandle() : base(ownsHandle: true)
    {
    }

    public int Size { get; private init; }

    public static IconHandle FromBitmap(BitmapSource bitmap)
    {
        // Icons store straight (not premultiplied) alpha in top-down 32-bit BGRA.
        var source = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var header = new NativeMethods.BitmapInfoHeader
        {
            Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        var color = NativeMethods.CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        // The AND mask is unused for 32-bit icons with alpha, but must exist; all zeros means "opaque".
        var mask = NativeMethods.CreateBitmap(width, height, 1, 1, new byte[(width + 15) / 16 * 2 * height]);
        try
        {
            if (mask == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            var info = new NativeMethods.IconInfo { IsIcon = true, Mask = mask, Color = color };
            var icon = new IconHandle { Size = width };
            icon.SetHandle(NativeMethods.CreateIconIndirect(ref info));
            if (icon.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return icon;
        }
        finally
        {
            NativeMethods.DeleteObject(color);
            if (mask != IntPtr.Zero) NativeMethods.DeleteObject(mask);
        }
    }

    protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool IsIcon;
            public int HotspotX;
            public int HotspotY;
            public IntPtr Mask;
            public IntPtr Color;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref IconInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr icon);
    }
}

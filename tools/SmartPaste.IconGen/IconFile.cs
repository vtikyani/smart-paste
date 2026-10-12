using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartPaste.IconGen;

// Writes a Windows .ico file. The 256 px image is stored as PNG (Windows Vista and later read that, and it keeps the
// file small); smaller sizes are stored as classic 32-bit bitmaps with alpha, which every reader understands.
internal static class IconFile
{
    public static void Write(string path, IReadOnlyList<BitmapSource> images)
    {
        var payloads = images.Select(image => image.PixelWidth >= 256 ? Png(image) : Dib(image)).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // ICONDIR
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        // ICONDIRENTRY for each image; data follows the directory.
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var size = images[i].PixelWidth;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }
        foreach (var payload in payloads) writer.Write(payload);
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }

    // BITMAPINFOHEADER, then the color rows bottom-up (straight-alpha BGRA), then the 1-bit AND mask bottom-up
    // (set where a pixel is fully transparent, for readers that ignore alpha).
    private static byte[] Dib(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        var maskStride = (width + 31) / 32 * 4;

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(stride * height + maskStride * height);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var y = height - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
        for (var y = height - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (var x = 0; x < width; x++)
                if (pixels[y * stride + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(row);
        }
        return buffer.ToArray();
    }
}

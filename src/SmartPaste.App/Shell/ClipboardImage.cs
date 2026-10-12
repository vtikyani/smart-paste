using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartPaste.App.Shell;

// Pixels decoded from a DIB: 32-bit BGRA, top row first. HasAlpha is false when the fourth byte means nothing.
internal sealed record DecodedImage(int Width, int Height, byte[] Bgra, bool HasAlpha);

// Copied screenshots arrive as PNG, CF_DIBV5, or CF_DIB. All of them leave as a PNG no larger than MaxSide on its
// longest side, which is what the OCR step reads. Clipboard data comes from other apps, so every size and offset is
// checked before it is used.
internal static class ClipboardImage
{
    public const int MaxSide = 2000;

    // Images with more pixels than this are refused before decoding.
    public const long MaxSourcePixels = 64L * 1024 * 1024;

    private const uint BiRgb = 0;
    private const uint BiBitfields = 3;
    private const uint BiJpeg = 4;
    private const uint BiPng = 5;
    private const uint BiAlphaBitfields = 6;

    // A CF_DIB or CF_DIBV5 payload as PNG. Null when it is not a bitmap this can read.
    public static byte[]? DibToPng(byte[] dib)
    {
        if (TryGetEmbeddedImage(dib, out var embedded)) return NormalizeEncoded(embedded);
        var image = DecodeDib(dib);
        if (image is null) return null;
        var format = image.HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, format, null, image.Bgra, image.Width * 4);
        return EncodePng(Downscale(source));
    }

    // A copied PNG, downscaled when larger than MaxSide. Null when it does not decode.
    public static byte[]? NormalizePng(byte[] png) => NormalizeEncoded(png);

    // The size that fits within maxSide on the longest side, keeping the aspect ratio.
    public static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        var scale = (double)maxSide / longest;
        return (Math.Clamp((int)Math.Round(width * scale), 1, maxSide), Math.Clamp((int)Math.Round(height * scale), 1, maxSide));
    }

    // Header parsing and pixel conversion, without any imaging library.
    internal static DecodedImage? DecodeDib(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) return null;
        var headerSize = ReadInt32(dib, 0);
        // BITMAPINFOHEADER, V2, V3, V4, V5.
        if (headerSize is not (40 or 52 or 56 or 108 or 124) || headerSize > dib.Length) return null;

        var width = ReadInt32(dib, 4);
        var rawHeight = ReadInt32(dib, 8);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (width <= 0 || rawHeight == 0 || rawHeight == int.MinValue || planes != 1) return null;
        var height = Math.Abs(rawHeight);
        if ((long)width * height > MaxSourcePixels) return null;

        uint red = 0, green = 0, blue = 0, alpha = 0;
        var maskBytes = 0;
        var bitfields = compression is BiBitfields or BiAlphaBitfields;
        if (bitfields)
        {
            if (bitCount is not (16 or 32)) return null;
            // A plain BITMAPINFOHEADER is followed by the masks; the larger headers contain them.
            if (headerSize == 40) maskBytes = compression == BiAlphaBitfields ? 16 : 12;
            if (dib.Length < 40 + Math.Max(maskBytes, 12)) return null;
            red = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
            green = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
            blue = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
            if ((headerSize == 40 && compression == BiAlphaBitfields) || headerSize >= 56)
                alpha = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            if (red == 0 || green == 0 || blue == 0) return null;
        }
        else if (compression == BiRgb)
        {
            if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32)) return null;
            if (bitCount == 16)
            {
                // 16-bit BI_RGB is 5-5-5.
                red = 0x7C00;
                green = 0x03E0;
                blue = 0x001F;
            }
            // V3 and later headers can mark the fourth byte of a 32-bit BI_RGB image as alpha.
            if (bitCount == 32 && headerSize >= 56) alpha = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
        }
        else
        {
            return null;
        }

        // The color table: the palette for 8 bits and fewer; for deeper images an optional table that only needs skipping.
        var maxEntries = bitCount <= 8 ? 1 << bitCount : 4096;
        var paletteEntries = (int)Math.Min(colorsUsed == 0 && bitCount <= 8 ? (uint)maxEntries : colorsUsed, (uint)maxEntries);
        var paletteOffset = headerSize + maskBytes;
        var stride = ((long)width * bitCount + 31) / 32 * 4;
        var imageBytes = stride * height;
        long offset = paletteOffset + paletteEntries * 4L;
        if (offset + imageBytes > dib.Length) return null;

        // Some producers repeat the masks after a larger header, as if it were a plain BITMAPINFOHEADER. Skip them
        // only when the leftover bytes are exactly those masks.
        var extra = dib.Length - offset - imageBytes;
        if (bitfields && headerSize > 40 && extra is 12 or 16 &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[(int)offset..]) == red &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[((int)offset + 4)..]) == green &&
            BinaryPrimitives.ReadUInt32LittleEndian(dib[((int)offset + 8)..]) == blue)
        {
            offset += extra;
        }

        var palette = dib.Slice(paletteOffset, paletteEntries * 4);
        var pixels = new byte[(long)width * height * 4];
        var anyAlpha = false;
        var allOpaque = true;
        for (var row = 0; row < height; row++)
        {
            // Bottom-up unless the height is negative.
            var sourceRow = rawHeight < 0 ? row : height - 1 - row;
            var source = dib.Slice((int)(offset + sourceRow * stride), (int)stride);
            var target = pixels.AsSpan(row * width * 4, width * 4);
            switch (bitCount)
            {
                case 32 when !bitfields:
                    source[..(width * 4)].CopyTo(target);
                    for (var x = 3; x < target.Length; x += 4)
                    {
                        anyAlpha |= target[x] != 0;
                        allOpaque &= target[x] == 0xFF;
                    }
                    break;
                case 24:
                    for (var x = 0; x < width; x++)
                    {
                        target[x * 4] = source[x * 3];
                        target[x * 4 + 1] = source[x * 3 + 1];
                        target[x * 4 + 2] = source[x * 3 + 2];
                        target[x * 4 + 3] = 0xFF;
                    }
                    break;
                case 16 or 32:
                    for (var x = 0; x < width; x++)
                    {
                        var value = bitCount == 16
                            ? BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..])
                            : BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                        target[x * 4] = Channel(value, blue);
                        target[x * 4 + 1] = Channel(value, green);
                        target[x * 4 + 2] = Channel(value, red);
                        var a = alpha == 0 ? (byte)0xFF : Channel(value, alpha);
                        target[x * 4 + 3] = a;
                        anyAlpha |= a != 0;
                        allOpaque &= a == 0xFF;
                    }
                    break;
                default:
                    if (!ExpandIndexed(source, target, width, bitCount, palette)) return null;
                    break;
            }
        }

        // A fourth byte counts as alpha only when the header says so and the values are not all zero (GDI leaves
        // them zero) or all opaque.
        var hasAlpha = bitCount == 32 && (alpha != 0 || (headerSize == 40 && !bitfields)) && anyAlpha && !allOpaque;
        if (bitCount == 32 && !hasAlpha)
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;
        return new DecodedImage(width, height, pixels, hasAlpha);
    }

    private static bool ExpandIndexed(ReadOnlySpan<byte> source, Span<byte> target, int width, int bitCount, ReadOnlySpan<byte> palette)
    {
        var entries = palette.Length / 4;
        var perByte = 8 / bitCount;
        var mask = (1 << bitCount) - 1;
        for (var x = 0; x < width; x++)
        {
            var packed = source[x / perByte];
            // The leftmost pixel sits in the highest bits.
            var shift = (perByte - 1 - x % perByte) * bitCount;
            var index = (packed >> shift) & mask;
            if (index >= entries) return false;
            target[x * 4] = palette[index * 4];
            target[x * 4 + 1] = palette[index * 4 + 1];
            target[x * 4 + 2] = palette[index * 4 + 2];
            target[x * 4 + 3] = 0xFF;
        }
        return true;
    }

    // One color channel scaled to 8 bits, whatever the width and position of its mask.
    private static byte Channel(uint value, uint mask)
    {
        var shift = BitOperations.TrailingZeroCount(mask);
        ulong maximum = mask >> shift;
        ulong raw = (value & mask) >> shift;
        return (byte)((raw * 255 + maximum / 2) / maximum);
    }

    // BI_PNG and BI_JPEG DIBs carry a whole encoded image after the header.
    private static bool TryGetEmbeddedImage(byte[] dib, out byte[] embedded)
    {
        embedded = [];
        if (dib.Length < 40) return false;
        var headerSize = ReadInt32(dib, 0);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        if (compression is not (BiPng or BiJpeg) || headerSize is < 40 or > 124 || headerSize >= dib.Length) return false;
        var size = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(20));
        var available = dib.Length - headerSize;
        var length = size == 0 || size > available ? available : (int)size;
        embedded = dib.AsSpan(headerSize, length).ToArray();
        return true;
    }

    private static byte[]? NormalizeEncoded(byte[] encoded)
    {
        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            // No caching: the frame size is known before any pixel is decoded, so oversized images cost nothing.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > MaxSourcePixels)
                return null;
            if (decoder is PngBitmapDecoder && Math.Max(frame.PixelWidth, frame.PixelHeight) <= MaxSide) return encoded;
            return EncodePng(Downscale(frame));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException
                                       or COMException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static BitmapSource Downscale(BitmapSource source)
    {
        var (width, height) = FitWithin(source.PixelWidth, source.PixelHeight, MaxSide);
        if (width == source.PixelWidth && height == source.PixelHeight) return source;
        var scaled = new TransformedBitmap(source, new ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
        // Rounding inside the scaler can leave a side one pixel over; trim it to the computed size.
        if (scaled.PixelWidth > width || scaled.PixelHeight > height)
            return new CroppedBitmap(scaled, new System.Windows.Int32Rect(0, 0, Math.Min(width, scaled.PixelWidth), Math.Min(height, scaled.PixelHeight)));
        return scaled;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}

using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

// Copied screenshots arrive in many DIB layouts. These build each layout by hand and check the pixels that come out.
public sealed class ClipboardImageTests
{
    private const uint BiRgb = 0;
    private const uint BiRle8 = 1;
    private const uint BiBitfields = 3;
    private const uint BiPng = 5;

    // Pixel colors as BGRA bytes.
    private static readonly byte[] Red = [0x00, 0x00, 0xFF, 0xFF];
    private static readonly byte[] Green = [0x00, 0xFF, 0x00, 0xFF];
    private static readonly byte[] Blue = [0xFF, 0x00, 0x00, 0xFF];
    private static readonly byte[] White = [0xFF, 0xFF, 0xFF, 0xFF];
    private static readonly byte[] Black = [0x00, 0x00, 0x00, 0xFF];

    private static byte[] BuildDib(
        int headerSize, int width, int height, ushort bitCount, uint compression, byte[] pixels,
        uint[]? headerMasks = null, uint[]? masksAfterHeader = null, byte[]? colorTable = null, uint colorsUsed = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(headerSize);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write(bitCount);
        writer.Write(compression);
        writer.Write((uint)pixels.Length);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(colorsUsed);
        writer.Write(0u);
        if (headerSize > 40)
        {
            var masks = headerMasks ?? [0, 0, 0, 0];
            writer.Write(masks[0]);
            writer.Write(masks[1]);
            writer.Write(masks[2]);
            if (headerSize >= 56) writer.Write(masks[3]);
            while (stream.Length < headerSize) writer.Write((byte)0);
        }
        foreach (var mask in masksAfterHeader ?? []) writer.Write(mask);
        if (colorTable is not null) writer.Write(colorTable);
        writer.Write(pixels);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Rows(params byte[][] rows) => rows.SelectMany(row => row).ToArray();

    private static byte[] Pixel(DecodedImage image, int x, int y) => image.Bgra.AsSpan((y * image.Width + x) * 4, 4).ToArray();

    // A 3x2 picture: red, green, blue on top; white, black, blue below.
    private static void AssertThreeByTwo(DecodedImage? image)
    {
        Assert.NotNull(image);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(Green, Pixel(image, 1, 0));
        Assert.Equal(Blue, Pixel(image, 2, 0));
        Assert.Equal(White, Pixel(image, 0, 1));
        Assert.Equal(Black, Pixel(image, 1, 1));
        Assert.Equal(Blue, Pixel(image, 2, 1));
    }

    private static byte[] Bgr(byte[] color) => color[..3];

    [Fact]
    public void Reads24BitBottomUp()
    {
        // Rows are padded to four bytes, and the bottom row comes first.
        var bottom = Rows(Bgr(White), Bgr(Black), Bgr(Blue), [0, 0, 0]);
        var top = Rows(Bgr(Red), Bgr(Green), Bgr(Blue), [0, 0, 0]);
        var image = ClipboardImage.DecodeDib(BuildDib(40, 3, 2, 24, BiRgb, Rows(bottom, top)));
        AssertThreeByTwo(image);
        Assert.False(image!.HasAlpha);
    }

    [Fact]
    public void Reads32BitTopDownAndTreatsZeroAlphaAsOpaque()
    {
        static byte[] X(byte[] color) => [color[0], color[1], color[2], 0];
        var pixels = Rows(X(Red), X(Green), X(Blue), X(White), X(Black), X(Blue));
        var image = ClipboardImage.DecodeDib(BuildDib(40, 3, -2, 32, BiRgb, pixels));
        AssertThreeByTwo(image);
        Assert.False(image!.HasAlpha);
    }

    [Fact]
    public void Keeps32BitAlphaWhenItCarriesInformation()
    {
        byte[] seeThrough = [0x10, 0x20, 0x30, 0x80];
        var pixels = Rows(seeThrough, Red);
        var image = ClipboardImage.DecodeDib(BuildDib(40, 2, -1, 32, BiRgb, pixels));
        Assert.NotNull(image);
        Assert.True(image.HasAlpha);
        Assert.Equal(seeThrough, Pixel(image, 0, 0));
    }

    [Fact]
    public void ReadsBitfieldsWithMasksAfterAPlainHeader()
    {
        var pixels = Rows(Red, Green, Blue, White, Black, Blue);
        var dib = BuildDib(40, 3, -2, 32, BiBitfields, pixels, masksAfterHeader: [0x00FF0000, 0x0000FF00, 0x000000FF]);
        AssertThreeByTwo(ClipboardImage.DecodeDib(dib));
    }

    [Fact]
    public void ReadsAV5HeaderWithItsOwnMasksInAnyOrder()
    {
        // RGBA byte order with a real alpha channel, as some browsers write it.
        static byte[] Rgba(byte r, byte g, byte b, byte a) => [r, g, b, a];
        var pixels = Rows(Rgba(0xFF, 0, 0, 0xFF), Rgba(0, 0, 0xFF, 0x40));
        var dib = BuildDib(124, 2, -1, 32, BiBitfields, pixels, headerMasks: [0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000]);
        var image = ClipboardImage.DecodeDib(dib);
        Assert.NotNull(image);
        Assert.True(image.HasAlpha);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0x40 }, Pixel(image, 1, 0));
    }

    [Fact]
    public void ToleratesMasksRepeatedAfterAV5Header()
    {
        var pixels = Rows(Red, Green, Blue, White, Black, Blue);
        uint[] masks = [0x00FF0000, 0x0000FF00, 0x000000FF];
        var dib = BuildDib(124, 3, -2, 32, BiBitfields, pixels, headerMasks: [.. masks, 0], masksAfterHeader: masks);
        AssertThreeByTwo(ClipboardImage.DecodeDib(dib));
    }

    [Fact]
    public void Reads16Bit565FromAV4Header()
    {
        static byte[] Word(ushort value) => BitConverter.GetBytes(value);
        // Red, half green, blue, then two bytes of row padding.
        var pixels = Rows(Word(0xF800), Word(0x0400), Word(0x001F), [0, 0]);
        var dib = BuildDib(108, 3, 1, 16, BiBitfields, pixels, headerMasks: [0xF800, 0x07E0, 0x001F, 0]);
        var image = ClipboardImage.DecodeDib(dib);
        Assert.NotNull(image);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(new byte[] { 0x00, 130, 0x00, 0xFF }, Pixel(image, 1, 0));
        Assert.Equal(Blue, Pixel(image, 2, 0));
    }

    [Fact]
    public void Reads16BitRgbAs555()
    {
        var pixels = Rows(BitConverter.GetBytes((ushort)0x7C00), BitConverter.GetBytes((ushort)0x03E0));
        var image = ClipboardImage.DecodeDib(BuildDib(40, 2, 1, 16, BiRgb, pixels));
        Assert.NotNull(image);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(Green, Pixel(image, 1, 0));
    }

    [Fact]
    public void Reads8BitWithAShortPalette()
    {
        byte[] palette = [.. Bgr(Red), 0, .. Bgr(Green), 0, .. Bgr(Blue), 0];
        var pixels = Rows([0, 1, 2, 1]);
        var image = ClipboardImage.DecodeDib(BuildDib(40, 4, 1, 8, BiRgb, pixels, colorTable: palette, colorsUsed: 3));
        Assert.NotNull(image);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(Green, Pixel(image, 1, 0));
        Assert.Equal(Blue, Pixel(image, 2, 0));
        Assert.Equal(Green, Pixel(image, 3, 0));
    }

    [Fact]
    public void Reads4BitAnd1BitPalettesHighBitsFirst()
    {
        byte[] palette = [.. Bgr(Black), 0, .. Bgr(White), 0];

        // 4 bits: pixels 1, 0, 1 packed as 0x10, 0x10, then padding.
        var four = ClipboardImage.DecodeDib(BuildDib(40, 3, 1, 4, BiRgb, [0x10, 0x10, 0, 0], colorTable: palette, colorsUsed: 2));
        Assert.NotNull(four);
        Assert.Equal(White, Pixel(four, 0, 0));
        Assert.Equal(Black, Pixel(four, 1, 0));
        Assert.Equal(White, Pixel(four, 2, 0));

        // 1 bit, full two-entry palette implied: 1010000011 over ten pixels.
        var one = ClipboardImage.DecodeDib(BuildDib(40, 10, 1, 1, BiRgb, [0b1010_0000, 0b1100_0000, 0, 0], colorTable: palette));
        Assert.NotNull(one);
        Assert.Equal(White, Pixel(one, 0, 0));
        Assert.Equal(Black, Pixel(one, 1, 0));
        Assert.Equal(White, Pixel(one, 2, 0));
        Assert.Equal(Black, Pixel(one, 7, 0));
        Assert.Equal(White, Pixel(one, 8, 0));
        Assert.Equal(White, Pixel(one, 9, 0));
    }

    [Fact]
    public void SkipsTheOptionalColorTableOfDeepImages()
    {
        var table = new byte[8];
        var pixels = Rows(Bgr(Red), [0]);
        var image = ClipboardImage.DecodeDib(BuildDib(40, 1, 1, 24, BiRgb, pixels, colorTable: table, colorsUsed: 2));
        Assert.NotNull(image);
        Assert.Equal(Red, Pixel(image, 0, 0));
    }

    [Fact]
    public void RefusesMalformedOrUnsupportedBitmaps()
    {
        var valid = BuildDib(40, 3, 2, 24, BiRgb, new byte[24]);
        Assert.NotNull(ClipboardImage.DecodeDib(valid));

        Assert.Null(ClipboardImage.DecodeDib(valid.AsSpan(0, valid.Length - 1)));
        Assert.Null(ClipboardImage.DecodeDib(new byte[20]));
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 3, 2, 8, BiRle8, new byte[24])));
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 0, 2, 24, BiRgb, new byte[24])));
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 3, 2, 12, BiRgb, new byte[24])));
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(64, 3, 2, 24, BiRgb, new byte[24])));
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 3, 2, 32, BiBitfields, new byte[24], masksAfterHeader: [0, 0, 0])));
        // Larger than any screenshot: refused before anything is allocated.
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 100_000, 100_000, 24, BiRgb, [])));
        // A palette index past the end of the palette.
        Assert.Null(ClipboardImage.DecodeDib(BuildDib(40, 1, 1, 8, BiRgb, [5, 0, 0, 0], colorTable: new byte[8], colorsUsed: 2)));
    }

    [Fact]
    public void ConvertsADibToAPngWithTheSamePixels()
    {
        var bottom = Rows(Bgr(White), Bgr(Black), Bgr(Blue), [0, 0, 0]);
        var top = Rows(Bgr(Red), Bgr(Green), Bgr(Blue), [0, 0, 0]);
        var png = ClipboardImage.DibToPng(BuildDib(40, 3, 2, 24, BiRgb, Rows(bottom, top)));

        var (width, height, pixels) = DecodePng(png);
        Assert.Equal((3, 2), (width, height));
        Assert.Equal(Red, pixels[..4]);
        Assert.Equal(Black, pixels[16..20]);
    }

    [Fact]
    public void DownscalesSoTheLongestSideIsAtMost2000()
    {
        var pixels = new byte[2400 * 30 * 4];
        var png = ClipboardImage.DibToPng(BuildDib(40, 2400, -30, 32, BiRgb, pixels));
        var (width, height, _) = DecodePng(png);
        Assert.Equal(2000, width);
        Assert.Equal(25, height);
    }

    [Fact]
    public void KeepsASmallPngAsItIsAndShrinksALargeOne()
    {
        var small = EncodePng(40, 20);
        Assert.Same(small, ClipboardImage.NormalizePng(small));

        var large = ClipboardImage.NormalizePng(EncodePng(3001, 10));
        var (width, height, _) = DecodePng(large);
        Assert.Equal(2000, width);
        Assert.Equal(7, height);
    }

    [Fact]
    public void RejectsBytesThatAreNotAnImage()
    {
        Assert.Null(ClipboardImage.NormalizePng([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]));
        Assert.Null(ClipboardImage.NormalizePng([]));
    }

    [Fact]
    public void ReadsAPngEmbeddedInADib()
    {
        var png = EncodePng(5, 4);
        var dib = BuildDib(40, 5, 4, 0, BiPng, png);
        var (width, height, _) = DecodePng(ClipboardImage.DibToPng(dib));
        Assert.Equal((5, 4), (width, height));
    }

    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(4000, 3000, 2000, 1500)]
    [InlineData(3000, 4000, 1500, 2000)]
    [InlineData(5000, 1, 2000, 1)]
    [InlineData(2001, 2001, 2000, 2000)]
    public void FitsWithinTheLimit(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), ClipboardImage.FitWithin(width, height, 2000));
    }

    private static byte[] EncodePng(int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static (int Width, int Height, byte[] Pixels) DecodePng(byte[]? png)
    {
        Assert.NotNull(png);
        var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return (frame.PixelWidth, frame.PixelHeight, pixels);
    }
}

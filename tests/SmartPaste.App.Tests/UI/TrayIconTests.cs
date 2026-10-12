using System.Windows.Media.Imaging;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Tray;

// The tray glyph for every status, at the icon size of every display scale, on dark and light taskbars.
public sealed class TrayIconTests
{
    // Small-icon sizes for 100 % to 300 % display scale.
    public static TheoryData<int> Sizes => new() { 16, 20, 24, 28, 32, 40, 48 };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryStatusRendersAtExactlyTheIconSize(int size) => WpfHost.Run(() =>
    {
        foreach (var theme in new[] { TaskbarTheme.Dark, TaskbarTheme.Light })
            foreach (var status in Enum.GetValues<TrayStatus>())
                for (var frame = 0; frame < TrayIconRenderer.FrameCount(status); frame++)
                {
                    var bitmap = TrayIconRenderer.Render(status, theme, size, frame);
                    Assert.Equal(size, bitmap.PixelWidth);
                    Assert.Equal(size, bitmap.PixelHeight);
                    Assert.True(UiRender.VisiblePixels(bitmap) > size, $"{status} {theme} {size}px frame {frame}");
                }
    });

    [Theory]
    [InlineData(false, 255, 255, 255)]
    [InlineData(true, 0x15, 0x17, 0x1F)]
    public void GlyphIsWhiteOnADarkTaskbarAndInkOnALightOne(bool lightTaskbar, byte r, byte g, byte b) => WpfHost.Run(() =>
    {
        var pixels = Opaque(TrayIconRenderer.Render(TrayStatus.Ready, Theme(lightTaskbar), 32));
        Assert.NotEmpty(pixels);
        Assert.All(pixels, p => Assert.Equal((r, g, b), (p.R, p.G, p.B)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmedShowsAHighlighterBadgeInTheLowerRight(bool lightTaskbar) => WpfHost.Run(() =>
    {
        var theme = Theme(lightTaskbar);
        var bitmap = TrayIconRenderer.Render(TrayStatus.Armed, theme, 32);
        Assert.Contains(Opaque(bitmap), p => p.X >= 20 && p.Y >= 20 && IsYellow(p));
        Assert.DoesNotContain(Opaque(TrayIconRenderer.Render(TrayStatus.Ready, theme, 32)), IsYellow);
    });

    [Fact]
    public void AttentionBadgeIsAmberNotHighlighterYellow() => WpfHost.Run(() =>
    {
        var pixels = Opaque(TrayIconRenderer.Render(TrayStatus.Attention, TaskbarTheme.Dark, 32));
        Assert.Contains(pixels, p => p.R > 240 && p.G is > 130 and < 180 && p.B < 60);
        Assert.DoesNotContain(pixels, IsYellow);
    });

    [Fact]
    public void ParsingBadgePulsesOnlyWhileParsing() => WpfHost.Run(() =>
    {
        Assert.Equal(TrayIconRenderer.PulseFrames, TrayIconRenderer.FrameCount(TrayStatus.Parsing));
        Assert.All(Enum.GetValues<TrayStatus>().Where(s => s != TrayStatus.Parsing), s => Assert.Equal(1, TrayIconRenderer.FrameCount(s)));
        var dim = UiRender.Pixels(TrayIconRenderer.Render(TrayStatus.Parsing, TaskbarTheme.Dark, 32, frame: 0));
        var full = UiRender.Pixels(TrayIconRenderer.Render(TrayStatus.Parsing, TaskbarTheme.Dark, 32, frame: TrayIconRenderer.PulseFrames / 2));
        Assert.NotEqual(dim, full);
    });

    [Theory]
    [InlineData(TrayStatus.Paused)]
    [InlineData(TrayStatus.Off)]
    public void PausedAndOffDimTheGlyph(TrayStatus status) => WpfHost.Run(() =>
    {
        // The board's left edge, away from the badge and the slash: half opacity instead of solid.
        var bitmap = TrayIconRenderer.Render(status, TaskbarTheme.Dark, 32);
        var edge = UiRender.Pixels(bitmap);
        var alpha = edge[(16 * 32 + 6) * 4 + 3];
        Assert.InRange(alpha, 100, 150);
    });

    [Fact]
    public void IconHandleWrapsTheRenderedPixels() => WpfHost.Run(() =>
    {
        using var icon = IconHandle.FromBitmap(TrayIconRenderer.Render(TrayStatus.Armed, TaskbarTheme.Dark, 20));
        Assert.False(icon.IsInvalid);
        Assert.Equal(20, icon.Size);
    });

    [Fact]
    public void AppIconHoldsEverySizeWindowsAsksFor() => WpfHost.Run(() =>
    {
        var path = Path.Combine(RepoRoot(), "src", "SmartPaste.App", "Assets", "SmartPaste.ico");
        using var stream = File.OpenRead(path);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], decoder.Frames.Select(f => f.PixelWidth).Order());
        Assert.All(decoder.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
    });

    private static TaskbarTheme Theme(bool light) => light ? TaskbarTheme.Light : TaskbarTheme.Dark;

    private static bool IsYellow(Px p) => p.R > 230 && p.G > 190 && p.B < 110;

    private static List<Px> Opaque(BitmapSource bitmap)
    {
        var bytes = UiRender.Pixels(bitmap);
        var list = new List<Px>();
        for (var i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i + 3] != 255) continue;
            var index = i / 4;
            list.Add(new Px(index % bitmap.PixelWidth, index / bitmap.PixelWidth, bytes[i + 2], bytes[i + 1], bytes[i]));
        }
        return list;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SmartPaste.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("The repository root was not found above the test output folder.");
    }

    private readonly record struct Px(int X, int Y, byte R, byte G, byte B);
}

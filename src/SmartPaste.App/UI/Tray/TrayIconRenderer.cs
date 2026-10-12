using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Brand;

namespace SmartPaste.App.UI.Tray;

// The taskbar's own theme, which can differ from the app's: Windows lets the taskbar be dark while apps are light.
internal enum TaskbarTheme { Dark, Light }

// Draws the tray icon for one status at an exact pixel size: the monochrome glyph (white on a dark taskbar, ink on
// a light one) plus a small status badge in the lower right. Badges are cut out of the glyph with a clear gap, the
// way Windows draws its own status overlays, so they read against any taskbar color.
internal static class TrayIconRenderer
{
    // Frames in one pulse of the "reading your copy" badge; shown at about 8 frames a second.
    public const int PulseFrames = 8;

    private const double DimmedOpacity = 0.5;

    public static BitmapSource Render(TrayStatus status, TaskbarTheme theme, int pixelSize, int frame = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 16);
        var palette = Palette.For(theme);
        var badge = Badge.For(pixelSize);
        Geometry glyph = BrandArt.CreateGlyph(pixelSize);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, pixelSize, pixelSize)));
            switch (status)
            {
                case TrayStatus.Ready:
                    dc.DrawGeometry(palette.Glyph, null, glyph);
                    break;

                case TrayStatus.Armed:
                    dc.DrawGeometry(palette.Glyph, null, Cut(glyph, badge.Knockout));
                    DrawDot(dc, badge, palette.Armed, palette.ArmedRing, scale: 1);
                    break;

                case TrayStatus.Parsing:
                {
                    // A slow breath: dim and slightly small at the start of the cycle, full at the middle.
                    var t = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (frame % PulseFrames) / PulseFrames);
                    dc.DrawGeometry(palette.Glyph, null, Cut(glyph, badge.Knockout));
                    dc.PushOpacity(0.35 + 0.65 * t);
                    DrawDot(dc, badge, palette.Armed, palette.ArmedRing, scale: 0.72 + 0.28 * t);
                    dc.Pop();
                    break;
                }

                case TrayStatus.Paused:
                    dc.PushOpacity(DimmedOpacity);
                    dc.DrawGeometry(palette.Glyph, null, Cut(glyph, badge.Knockout));
                    dc.Pop();
                    dc.DrawGeometry(palette.Glyph, null, PauseBars(badge));
                    break;

                case TrayStatus.Off:
                {
                    var slash = Slash(pixelSize, badge.Gap);
                    dc.PushOpacity(DimmedOpacity);
                    dc.DrawGeometry(palette.Glyph, null, Cut(glyph, slash.Knockout));
                    dc.Pop();
                    dc.DrawGeometry(palette.Glyph, null, slash.Line);
                    break;
                }

                case TrayStatus.Attention:
                {
                    var triangle = Triangle(badge);
                    dc.DrawGeometry(palette.Glyph, null, Cut(glyph, triangle.Knockout));
                    dc.DrawGeometry(palette.Attention, null, triangle.Shape);
                    if (triangle.Mark is { } mark) dc.DrawGeometry(palette.AttentionMark, null, mark);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // How many frames a status animates through (1 means a still icon).
    public static int FrameCount(TrayStatus status) => status == TrayStatus.Parsing ? PulseFrames : 1;

    private static Geometry Cut(Geometry glyph, Geometry knockout) =>
        Geometry.Combine(glyph, knockout, GeometryCombineMode.Exclude, null);

    private static void DrawDot(DrawingContext dc, Badge badge, Brush fill, Brush? ring, double scale)
    {
        var radius = badge.Radius * scale;
        if (ring is not null)
        {
            // The ring is the dot's own outer pixel in a deeper shade, so a yellow dot still has an edge on a
            // light taskbar.
            dc.DrawEllipse(ring, null, badge.Center, radius, radius);
            var inner = Math.Max(0.5, radius - badge.Ring);
            dc.DrawEllipse(fill, null, badge.Center, inner, inner);
        }
        else
        {
            dc.DrawEllipse(fill, null, badge.Center, radius, radius);
        }
    }

    private static Geometry PauseBars(Badge badge)
    {
        var size = badge.Diameter;
        var bar = Math.Max(1, Math.Round(size * 0.34));
        var left = badge.Box.Left;
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(left, badge.Box.Top, bar, size)));
        group.Children.Add(new RectangleGeometry(new Rect(left + size - bar, badge.Box.Top, bar, size)));
        return group;
    }

    // A diagonal stroke from the upper left to the lower right with a clear band on each side.
    private static (Geometry Line, Geometry Knockout) Slash(int size, double gap)
    {
        var width = Math.Max(1.25, size / 12.0);
        var from = new Point(size * 0.14, size * 0.14);
        var to = new Point(size * 0.86, size * 0.86);
        var line = new LineGeometry(from, to).GetWidenedPathGeometry(new Pen(Brushes.Black, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var knockout = new LineGeometry(from, to).GetWidenedPathGeometry(new Pen(Brushes.Black, width + 2 * gap) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        return (line, knockout);
    }

    // A rounded warning triangle; from 24 px up it carries an exclamation mark.
    private static (Geometry Shape, Geometry Knockout, Geometry? Mark) Triangle(Badge badge)
    {
        var box = badge.Box;
        var top = new Point(box.Left + box.Width / 2, box.Top - badge.Diameter * 0.08);
        var left = new Point(box.Left - badge.Diameter * 0.06, box.Bottom);
        var right = new Point(box.Right + badge.Diameter * 0.06, box.Bottom);
        var outline = new PathGeometry([new PathFigure(top, [new LineSegment(right, true), new LineSegment(left, true)], closed: true)]);
        var join = new Pen(Brushes.Black, Math.Max(1, badge.Diameter * 0.18)) { LineJoin = PenLineJoin.Round };
        var shape = Geometry.Combine(outline, outline.GetWidenedPathGeometry(join), GeometryCombineMode.Union, null);
        var knockout = shape.GetWidenedPathGeometry(new Pen(Brushes.Black, 2 * badge.Gap) { LineJoin = PenLineJoin.Round });
        knockout = Geometry.Combine(knockout, shape, GeometryCombineMode.Union, null);

        Geometry? mark = null;
        if (badge.Diameter >= 8)
        {
            var stroke = Math.Max(1, Math.Round(badge.Diameter / 8));
            var x = Math.Round(box.Left + box.Width / 2 - stroke / 2);
            var stemTop = Math.Round(box.Top + badge.Diameter * 0.3);
            var dotTop = Math.Round(box.Bottom - stroke - Math.Max(1, badge.Diameter * 0.1));
            var stemBottom = dotTop - Math.Max(1, Math.Round(stroke * 0.75));
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(x, stemTop, stroke, Math.Max(1, stemBottom - stemTop))));
            group.Children.Add(new RectangleGeometry(new Rect(x, dotTop, stroke, stroke)));
            mark = group;
        }
        return (shape, knockout, mark);
    }

    // Size and place of the badge for one icon size: a dot about a third of the icon, flush with the lower-right
    // corner, and the clear gap around it.
    private readonly record struct Badge(double Diameter, double Gap, double Ring)
    {
        public double Radius => Diameter / 2;
        public int IconSize { get; init; }
        public Point Center => new(IconSize - Radius, IconSize - Radius);
        public Rect Box => new(IconSize - Diameter, IconSize - Diameter, Diameter, Diameter);
        public Geometry Knockout => new EllipseGeometry(Center, Radius + Gap, Radius + Gap);

        // The gap grows in whole pixels like the glyph's stroke (1 px up to 150 % scale), so it never eats the
        // caret at 24 px.
        public static Badge For(int size) => new(
            Diameter: Math.Max(5, Math.Round(size * 0.34)),
            Gap: Math.Max(1, Math.Floor(size / 16.0 + 0.25)),
            Ring: Math.Max(1, Math.Round(size / 24.0))) { IconSize = size };
    }

    private sealed record Palette(Brush Glyph, Brush Armed, Brush? ArmedRing, Brush Attention, Brush AttentionMark)
    {
        private static readonly Palette OnDark = new(
            Glyph: Frozen(Colors.White),
            Armed: Frozen(BrandArt.Highlighter),
            ArmedRing: null,
            Attention: Frozen(Color.FromRgb(0xFF, 0x9F, 0x1C)),
            AttentionMark: Frozen(BrandArt.Ink));

        private static readonly Palette OnLight = new(
            Glyph: Frozen(BrandArt.Ink),
            Armed: Frozen(BrandArt.Highlighter),
            ArmedRing: Frozen(Color.FromRgb(0xB5, 0x86, 0x00)),
            Attention: Frozen(Color.FromRgb(0xE0, 0x72, 0x00)),
            AttentionMark: Frozen(Colors.White));

        public static Palette For(TaskbarTheme theme) => theme == TaskbarTheme.Light ? OnLight : OnDark;

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

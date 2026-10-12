using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace SmartPaste.App.UI.Brand;

// The Smart Paste mark: a clipboard whose paper holds a few form lines. One line is marked with a highlighter
// stroke and a text caret at its end: "this is the value that goes here".
//
// Everything is vector geometry, hinted to whole pixels for each exact icon size so small sizes stay crisp,
// with detail removed as the size drops. tools/SmartPaste.IconGen compiles this file too (it renders the app
// icon from it), so it depends on WPF only.
public static class BrandArt
{
    public static readonly Color Ink = Color.FromRgb(0x15, 0x17, 0x1F);
    public static readonly Color Paper = Color.FromRgb(0xFB, 0xFA, 0xF7);
    public static readonly Color Highlighter = Color.FromRgb(0xFF, 0xD4, 0x3B);

    // The full-color mark is designed on a 256-unit square; the monochrome glyph on a 16-unit square.
    public const double LogoUnits = 256;
    public const double GlyphUnits = 16;

    // The board is lit from above so its edge still shows on dark backgrounds.
    private static readonly Color BoardTop = Color.FromRgb(0x3A, 0x3F, 0x4F);
    private static readonly Color ClipTop = Color.FromRgb(0xE6, 0xE9, 0xEE);
    private static readonly Color ClipBottom = Color.FromRgb(0xA7, 0xAD, 0xBA);
    private static readonly Color ClipSlot = Color.FromRgb(0x5D, 0x63, 0x72);
    private static readonly Color FormLine = Color.FromRgb(0xCF, 0xD3, 0xDA);

    // Resolution-independent mark for UI use (Brand.LogoImage). Frozen.
    public static Drawing Logo { get; } = Freeze(BuildLogo(LogoSpec.ForSize(null), LogoUnits, snap: false));

    // Resolution-independent monochrome glyph (16 x 16 units) as one filled geometry. Frozen.
    public static Geometry Glyph { get; } = Freeze(GlyphShapes.Build(GlyphUnits));

    // The full-color mark hinted for an exact pixel size, in a pixelSize x pixelSize square.
    public static Drawing CreateLogo(int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 16);
        return Freeze(BuildLogo(LogoSpec.ForSize(pixelSize), pixelSize, snap: true));
    }

    // The monochrome glyph hinted for an exact pixel size, in a pixelSize x pixelSize square.
    public static Geometry CreateGlyph(int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 16);
        return Freeze(GlyphShapes.Build(pixelSize));
    }

    // The full-color mark as a standalone SVG document (256 x 256 view box), for the README and the web.
    public static string ToSvg()
    {
        var spec = LogoSpec.ForSize(null);
        var svg = new StringBuilder()
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\">\n")
            .Append("  <title>Smart Paste</title>\n")
            .Append("  <defs>\n")
            .Append("    <linearGradient id=\"board\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">")
            .Append(Stop(0, BoardTop)).Append(Stop(1, Ink)).Append("</linearGradient>\n")
            .Append("    <linearGradient id=\"clip\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">")
            .Append(Stop(0, ClipTop)).Append(Stop(1, ClipBottom)).Append("</linearGradient>\n")
            .Append("  </defs>\n");
        foreach (var part in spec.Parts)
        {
            var fill = part.Fill switch
            {
                Fill.Board => "url(#board)",
                Fill.Clip => "url(#clip)",
                _ => Hex(SolidColor(part.Fill))
            };
            if (part.Polygon is { } points)
            {
                svg.Append("  <path d=\"M");
                svg.AppendJoin(" L", points.Select(p => F(p.X) + "," + F(p.Y)));
                svg.Append(" Z\" fill=\"").Append(fill).Append("\"/>\n");
            }
            else
            {
                var b = part.Box;
                svg.Append("  <rect x=\"").Append(F(b.X)).Append("\" y=\"").Append(F(b.Y))
                    .Append("\" width=\"").Append(F(b.W)).Append("\" height=\"").Append(F(b.H))
                    .Append("\" rx=\"").Append(F(b.R)).Append("\" fill=\"").Append(fill).Append("\"/>\n");
            }
        }
        return svg.Append("</svg>\n").ToString();

        static string Stop(double offset, Color c) => $"<stop offset=\"{F(offset)}\" stop-color=\"{Hex(c)}\"/>";
    }

    private static DrawingGroup BuildLogo(LogoSpec spec, double size, bool snap)
    {
        var k = size / LogoUnits;
        var group = new DrawingGroup();
        // A transparent frame keeps the drawing's bounds at the full square, padding included.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        foreach (var part in spec.Parts)
        {
            // Polygons keep their slanted sides but get whole-pixel top and bottom edges.
            Geometry geometry = part.Polygon is { } points
                ? Polygon(points.Select(p => new Point(p.X * k, snap ? Round(p.Y * k) : p.Y * k)))
                : RoundedRect(part.Box, k, size, snap);
            group.Children.Add(new GeometryDrawing(BrushFor(part.Fill, geometry.Bounds), null, geometry));
        }
        return group;
    }

    private static Brush BrushFor(Fill fill, Rect bounds) => fill switch
    {
        Fill.Board => Gradient(BoardTop, Ink, bounds),
        Fill.Clip => Gradient(ClipTop, ClipBottom, bounds),
        _ => new SolidColorBrush(SolidColor(fill))
    };

    private static Color SolidColor(Fill fill) => fill switch
    {
        Fill.Board => Ink,
        Fill.Clip => ClipBottom,
        Fill.ClipSlot => ClipSlot,
        Fill.Paper => Paper,
        Fill.Line => FormLine,
        Fill.Highlight => Highlighter,
        _ => Ink
    };

    private static LinearGradientBrush Gradient(Color top, Color bottom, Rect bounds) =>
        new(top, bottom, new Point(0, bounds.Top), new Point(0, bounds.Bottom)) { MappingMode = BrushMappingMode.Absolute };

    // Rounds a design-space box to whole pixels (at least one each way) so edges land on the pixel grid.
    // Horizontally centered parts keep equal margins on both sides, so the mark never leans at odd scales.
    private static Geometry RoundedRect(Box box, double k, double size, bool snap)
    {
        double left = box.X * k, top = box.Y * k, right = (box.X + box.W) * k, bottom = (box.Y + box.H) * k;
        if (snap)
        {
            top = Round(top);
            bottom = Math.Max(top + 1, Round(bottom));
            if (box.Centered)
            {
                left = Round(left);
                right = Math.Max(left + 1, size - left);
            }
            else
            {
                left = Round(left);
                right = Math.Max(left + 1, Round(right));
            }
        }
        var rect = new Rect(left, top, right - left, bottom - top);
        var radius = Math.Min(box.R * k, Math.Min(rect.Width, rect.Height) / 2);
        return new RectangleGeometry(rect, radius, radius);
    }

    private static Geometry Polygon(IEnumerable<Point> points)
    {
        var list = points.ToList();
        var figure = new PathFigure(list[0], list.Skip(1).Select(p => (PathSegment)new LineSegment(p, true)), closed: true);
        return new PathGeometry([figure]);
    }

    // Half rounds up, so the result never depends on banker's rounding.
    private static double Round(double value) => Math.Floor(value + 0.5);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private enum Fill { Board, Clip, ClipSlot, Paper, Line, Highlight, Ink }

    private readonly record struct Box(double X, double Y, double W, double H, double R, bool Centered = false);

    private sealed record Part(Fill Fill, Box Box, IReadOnlyList<Point>? Polygon = null);

    // Which parts the mark has at a given size. Small sizes drop the clip's slot, the caret's serifs, and most
    // form lines, and thicken what is left so it survives at a few pixels.
    private sealed record LogoSpec(IReadOnlyList<Part> Parts)
    {
        public static LogoSpec ForSize(int? pixelSize) => pixelSize switch
        {
            null => Full,
            <= 24 => Small,
            <= 40 => Medium,
            _ => Full
        };

        private static readonly LogoSpec Full = new(
        [
            new(Fill.Board, new(38, 34, 180, 208, 34, Centered: true)),
            new(Fill.Paper, new(56, 52, 144, 172, 16, Centered: true)),
            new(Fill.Clip, new(90, 12, 76, 54, 16, Centered: true)),
            new(Fill.ClipSlot, new(112, 26, 32, 10, 5, Centered: true)),
            new(Fill.Line, new(78, 90, 74, 10, 5)),
            new(Fill.Highlight, default, [new(76, 113), new(166, 113), new(159, 147), new(69, 147)]),
            new(Fill.Ink, new(84, 125, 62, 10, 5)),
            new(Fill.Ink, new(176, 105, 8, 50, 2)),
            new(Fill.Ink, new(167, 105, 26, 7, 3.5)),
            new(Fill.Ink, new(167, 148, 26, 7, 3.5)),
            new(Fill.Line, new(78, 171, 100, 10, 5)),
            new(Fill.Line, new(78, 195, 54, 10, 5))
        ]);

        private static readonly LogoSpec Medium = new(
        [
            new(Fill.Board, new(36, 30, 184, 212, 32, Centered: true)),
            new(Fill.Paper, new(56, 50, 144, 172, 14, Centered: true)),
            new(Fill.Clip, new(88, 8, 80, 56, 14, Centered: true)),
            new(Fill.Line, new(76, 88, 80, 12, 6)),
            new(Fill.Highlight, new(72, 112, 92, 36, 4)),
            new(Fill.Ink, new(176, 102, 14, 56, 2)),
            new(Fill.Line, new(76, 176, 92, 12, 6)),
            new(Fill.Line, new(76, 198, 52, 12, 6))
        ]);

        private static readonly LogoSpec Small = new(
        [
            new(Fill.Board, new(32, 28, 192, 208, 40, Centered: true)),
            new(Fill.Paper, new(48, 48, 160, 176, 16, Centered: true)),
            new(Fill.Clip, new(80, 8, 96, 56, 12, Centered: true)),
            new(Fill.Highlight, new(64, 96, 80, 48, 4)),
            new(Fill.Ink, new(160, 80, 16, 80, 2)),
            new(Fill.Line, new(64, 176, 80, 16, 8))
        ]);
    }

    // The monochrome glyph, built for a square of the given size (pixels for icons, units for the vector
    // version). It is designed on a 16 grid; each part keeps at least one whole pixel.
    private static class GlyphShapes
    {
        public static Geometry Build(double size)
        {
            var k = size / GlyphUnits;
            // 1 px up to 150 % scale, then 2 px, 3 px: heavier strokes would crowd the caret into the board edge.
            var stroke = Math.Max(1, Math.Floor(k + 0.25));
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            // Board outline: an outer rounded rectangle minus an inner one, one stroke apart.
            var outer = Centered(3, 3, 15, k, size);
            var inner = new Rect(outer.Left + stroke, outer.Top + stroke, outer.Width - 2 * stroke, outer.Height - 2 * stroke);
            var outerRadius = Math.Max(2, 2.5 * k);
            var innerRadius = Math.Max(1, outerRadius - stroke);
            group.Children.Add(Geometry.Combine(
                new RectangleGeometry(outer, outerRadius, outerRadius),
                new RectangleGeometry(inner, innerRadius, innerRadius),
                GeometryCombineMode.Exclude, null));

            // Clip straddling the board's top edge, drawn as an outline with a slot like the board itself; a
            // solid tab here reads as a jar lid or a battery terminal at tray sizes.
            var clip = Centered(5, 1, 4, k, size);
            var clipRadius = Math.Max(1, k);
            var slot = new Rect(clip.Left + stroke, clip.Top + stroke, clip.Width - 2 * stroke, Math.Max(1, clip.Height - 2 * stroke));
            group.Children.Add(Geometry.Combine(
                new RectangleGeometry(clip, clipRadius, clipRadius),
                new RectangleGeometry(slot),
                GeometryCombineMode.Exclude, null));

            // Text lines. The middle one is the highlighted value, with the caret right after it. All of it
            // stays in the upper left so a status badge in the lower right never covers it.
            group.Children.Add(new RectangleGeometry(Snap(5, 6, 9, 7, k)));
            var value = Snap(5, 8, 9, 10, k);
            group.Children.Add(new RectangleGeometry(value));
            // The caret sits one stroke after the value's snapped end, so the two never drift apart.
            var caret = Snap(10, 7, 11, 11, k);
            group.Children.Add(new RectangleGeometry(new Rect(value.Right + stroke, caret.Top, stroke, caret.Height)));
            group.Children.Add(new RectangleGeometry(Snap(5, 12, 8, 13, k)));
            return group;
        }

        // A box centered horizontally in the square, given its left margin, top, and bottom on the 16 grid.
        private static Rect Centered(double margin, double top, double bottom, double k, double size)
        {
            var m = Round(margin * k);
            var t = Round(top * k);
            return new Rect(m, t, Math.Max(1, size - 2 * m), Math.Max(1, Round(bottom * k) - t));
        }

        private static Rect Snap(double left, double top, double right, double bottom, double k)
        {
            var l = Round(left * k);
            var t = Round(top * k);
            return new Rect(l, t, Math.Max(1, Round(right * k) - l), Math.Max(1, Round(bottom * k) - t));
        }
    }
}

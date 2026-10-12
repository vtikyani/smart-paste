using SmartPaste.App.Services;

namespace SmartPaste.App.Shell;

// One line of recognized text and where it is on the screen.
internal readonly record struct TextLine(string Text, ScreenRect Bounds);

// Where to look for a field's label, and how recognized lines map back to the screen.
internal static class OcrGeometry
{
    // Labels sit to the left of a field on its row, or above it. Labels above a field start at its left edge and
    // run over it, so the area reaches part way over the field too; the field's own text is filtered out later.
    public const int LeftReach = 480;
    public const int OverFieldReach = 240;
    public const int AboveReach = 80;
    public const int MaxRowHeight = 120;

    public static ScreenRect LabelRegion(ScreenRect field, ScreenRect virtualScreen, ScreenRect? window)
    {
        var region = ScreenRect.FromLTRB(
            field.Left - LeftReach,
            field.Top - AboveReach,
            field.Left + Math.Min(field.Width, OverFieldReach),
            Math.Min(field.Bottom, field.Top + MaxRowHeight));
        region = Intersect(region, virtualScreen);
        // Text from a neighboring window is not this field's label.
        return window is { IsEmpty: false } bounds ? Intersect(region, bounds) : region;
    }

    public static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? ScreenRect.FromLTRB(left, top, right, bottom) : default;
    }

    // A rectangle in an image captured from region at the given scale, back in screen pixels.
    public static ScreenRect ToScreen(double x, double y, double width, double height, ScreenRect region, int scale) =>
        ScreenRect.FromLTRB(
            region.Left + (int)Math.Floor(x / scale),
            region.Top + (int)Math.Floor(y / scale),
            region.Left + (int)Math.Ceiling((x + width) / scale),
            region.Top + (int)Math.Ceiling((y + height) / scale));
}

// Picks the line most likely to be a field's label from what OCR found around it.
internal static class OcrLabelPicker
{
    public static string? Pick(IReadOnlyList<TextLine> lines, ScreenRect field)
    {
        var tolerance = Math.Max(4, field.Height / 3);
        var candidates = lines.Where(line => FieldRules.Clean(line.Text) is not null && !MostlyInside(line.Bounds, field)).ToList();

        // The nearest line on the field's row, ending before the field, is the label to its left.
        TextLine? label = null;
        foreach (var line in candidates)
            if (IsLeftOnRow(line.Bounds, field, tolerance) && (label is null || line.Bounds.Right > label.Value.Bounds.Right))
                label = line;

        // Otherwise the closest line above the field, preferring one that starts where the field starts.
        if (label is null)
            foreach (var line in candidates)
                if (IsAbove(line.Bounds, field, tolerance) && (label is null || IsBetterAbove(line, label.Value, field, tolerance)))
                    label = line;

        if (label is not { } chosen) return null;
        var text = FieldRules.Clean(chosen.Text)!;
        // A label wrapped onto two lines leaves a short tail ("1)", "*"): join it to the line before.
        if (text.Length < 3 && LineBefore(candidates, chosen) is { } before) text = FieldRules.Clean(before.Text) + " " + text;
        return FieldRules.Cut(text, FieldRules.MaxLabelLength);
    }

    private static bool IsLeftOnRow(ScreenRect line, ScreenRect field, int tolerance)
    {
        var middle = line.Top + line.Height / 2;
        return middle >= field.Top - tolerance && middle <= field.Bottom + tolerance && line.Right <= field.Left + tolerance;
    }

    private static bool IsAbove(ScreenRect line, ScreenRect field, int tolerance) =>
        line.Bottom <= field.Top + tolerance && line.Right > field.Left - 40 && line.Left < field.Right;

    private static bool IsBetterAbove(TextLine candidate, TextLine current, ScreenRect field, int tolerance)
    {
        if (Math.Abs(candidate.Bounds.Bottom - current.Bounds.Bottom) > tolerance) return candidate.Bounds.Bottom > current.Bounds.Bottom;
        return Math.Abs(candidate.Bounds.Left - field.Left) < Math.Abs(current.Bounds.Left - field.Left);
    }

    private static TextLine? LineBefore(List<TextLine> lines, TextLine line)
    {
        var tolerance = Math.Max(3, line.Bounds.Height / 3);
        var middle = line.Bounds.Top + line.Bounds.Height / 2;
        TextLine? sameRow = null;
        TextLine? above = null;
        foreach (var other in lines)
        {
            if (other == line) continue;
            var otherMiddle = other.Bounds.Top + other.Bounds.Height / 2;
            if (Math.Abs(otherMiddle - middle) <= tolerance && other.Bounds.Right <= line.Bounds.Left + tolerance &&
                line.Bounds.Left - other.Bounds.Right <= line.Bounds.Height * 3)
            {
                if (sameRow is null || other.Bounds.Right > sameRow.Value.Bounds.Right) sameRow = other;
            }
            else if (other.Bounds.Bottom <= line.Bounds.Top + tolerance && line.Bounds.Top - other.Bounds.Bottom <= line.Bounds.Height * 3 / 2 &&
                     other.Bounds.Left < line.Bounds.Right && other.Bounds.Right > line.Bounds.Left)
            {
                if (above is null || other.Bounds.Bottom > above.Value.Bounds.Bottom) above = other;
            }
        }
        return sameRow ?? above;
    }

    // The field's own text (its value or placeholder) is not its label.
    private static bool MostlyInside(ScreenRect line, ScreenRect field)
    {
        var overlap = OcrGeometry.Intersect(line, field);
        return !overlap.IsEmpty && (long)overlap.Width * overlap.Height * 2 > (long)line.Width * line.Height;
    }
}

internal static class OcrImage
{
    // OCR ignores transparency, and transparent pixels hide arbitrary colors (black, in premultiplied images). Laying
    // the picture on white keeps dark text on a transparent background readable.
    public static void FlattenOntoWhite(Span<byte> premultipliedBgra)
    {
        for (var i = 0; i + 3 < premultipliedBgra.Length; i += 4)
        {
            var cover = 255 - premultipliedBgra[i + 3];
            premultipliedBgra[i] = (byte)Math.Min(255, premultipliedBgra[i] + cover);
            premultipliedBgra[i + 1] = (byte)Math.Min(255, premultipliedBgra[i + 1] + cover);
            premultipliedBgra[i + 2] = (byte)Math.Min(255, premultipliedBgra[i + 2] + cover);
            premultipliedBgra[i + 3] = 255;
        }
    }
}

using SmartPaste.App.Services;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

public sealed class OcrLayoutTests
{
    private static readonly ScreenRect Screen = new(0, 0, 3840, 2160);
    private static readonly ScreenRect Field = new(1000, 500, 300, 30);

    private static TextLine Line(string text, int left, int top, int width = 120, int height = 20) =>
        new(text, new ScreenRect(left, top, width, height));

    // ---- Where to look ----

    [Fact]
    public void TheRegionCoversTheRowToTheLeftAndTheSpaceAbove()
    {
        var region = OcrGeometry.LabelRegion(Field, Screen, window: null);
        Assert.Equal(ScreenRect.FromLTRB(520, 420, 1240, 530), region);
    }

    [Fact]
    public void ATallFieldOnlyContributesItsTopToTheRegion()
    {
        var notes = new ScreenRect(1000, 500, 300, 400);
        Assert.Equal(620, OcrGeometry.LabelRegion(notes, Screen, window: null).Bottom);
    }

    [Fact]
    public void TheRegionStaysOnTheScreenAndInsideTheFieldsWindow()
    {
        var nearCorner = new ScreenRect(100, 30, 200, 24);
        Assert.Equal(ScreenRect.FromLTRB(0, 0, 300, 54), OcrGeometry.LabelRegion(nearCorner, Screen, window: null));

        var window = ScreenRect.FromLTRB(800, 300, 2000, 1200);
        Assert.Equal(ScreenRect.FromLTRB(800, 420, 1240, 530), OcrGeometry.LabelRegion(Field, Screen, window));
    }

    [Fact]
    public void RegionsWorkOnMonitorsLeftOfOrAboveThePrimary()
    {
        var virtualScreen = ScreenRect.FromLTRB(-1920, -1080, 1920, 1080);
        var field = new ScreenRect(-900, -400, 200, 30);
        Assert.Equal(ScreenRect.FromLTRB(-1380, -480, -700, -370), OcrGeometry.LabelRegion(field, virtualScreen, window: null));
    }

    [Fact]
    public void RectanglesThatDoNotOverlapIntersectToNothing()
    {
        Assert.True(OcrGeometry.Intersect(new ScreenRect(0, 0, 10, 10), new ScreenRect(20, 20, 5, 5)).IsEmpty);
        Assert.Equal(new ScreenRect(5, 5, 5, 5), OcrGeometry.Intersect(new ScreenRect(0, 0, 10, 10), new ScreenRect(5, 5, 10, 10)));
    }

    [Fact]
    public void ImageCoordinatesMapBackToTheScreen()
    {
        var region = ScreenRect.FromLTRB(520, 420, 1240, 530);
        Assert.Equal(ScreenRect.FromLTRB(570, 440, 600, 450), OcrGeometry.ToScreen(100, 40, 60, 20, region, 2));
        Assert.Equal(ScreenRect.FromLTRB(570, 440, 601, 451), OcrGeometry.ToScreen(100.4, 40.6, 60.8, 20.1, region, 2));
    }

    // ---- Which line is the label ----

    [Fact]
    public void TheLineToTheLeftOnTheFieldsRowIsTheLabel()
    {
        Assert.Equal("First name", OcrLabelPicker.Pick([Line("First name", 850, 505)], Field));
    }

    [Fact]
    public void OnTheRowTheLineNearestTheFieldWins()
    {
        var lines = new[] { Line("Contact", 560, 505, 80), Line("First name", 850, 505) };
        Assert.Equal("First name", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void WithNothingOnTheRowTheClosestLineAboveIsTheLabel()
    {
        var lines = new[] { Line("Shipping address", 1000, 430, 200), Line("Street", 1000, 470, 60) };
        Assert.Equal("Street", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void ARowLabelBeatsALineAbove()
    {
        var lines = new[] { Line("Section title", 1000, 470), Line("City", 920, 505, 40) };
        Assert.Equal("City", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void AboveTheFieldTheLineStartingWhereTheFieldStartsWins()
    {
        var lines = new[] { Line("Other column", 1200, 470), Line("Email", 1002, 470, 50) };
        Assert.Equal("Email", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void TheFieldsOwnTextIsNotItsLabel()
    {
        var lines = new[] { Line("you@example.com", 1005, 505, 150) };
        Assert.Null(OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void TextRightOfTheFieldOrFarBelowIsNotALabel()
    {
        var lines = new[] { Line("Help", 1320, 505, 40), Line("Next field", 1000, 560) };
        Assert.Null(OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void AWrappedLabelIsJoinedBackTogether()
    {
        var lines = new[] { Line("Street address (line", 1000, 450, 180), Line("1)", 1000, 474, 20) };
        Assert.Equal("Street address (line 1)", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void ARequiredMarkReadAsItsOwnLineJoinsTheLabelBeforeIt()
    {
        var lines = new[] { Line("Email", 850, 505, 60), Line("*", 915, 505, 8) };
        Assert.Equal("Email *", OcrLabelPicker.Pick(lines, Field));
    }

    [Fact]
    public void NoLinesMeansNoLabel()
    {
        Assert.Null(OcrLabelPicker.Pick([], Field));
        Assert.Null(OcrLabelPicker.Pick([Line("   ", 850, 505)], Field));
    }

    [Fact]
    public void LongLinesAreCut()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 40));
        var label = OcrLabelPicker.Pick([Line(text, 300, 505, 690)], Field);
        Assert.NotNull(label);
        Assert.True(label.Length <= FieldRules.MaxLabelLength);
    }

    // ---- Pixels for the engine ----

    [Fact]
    public void TransparencyIsLaidOnWhite()
    {
        byte[] pixels =
        [
            0, 0, 0, 0, // fully transparent
            0, 0, 255, 255, // opaque red
            0, 0, 0, 128, // half-transparent black (premultiplied)
            64, 64, 64, 128 // half-transparent gray (premultiplied)
        ];

        OcrImage.FlattenOntoWhite(pixels);

        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 255, 255, 127, 127, 127, 255, 191, 191, 191, 255 }, pixels);
    }
}

using SmartPaste.App.Services;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

public sealed class CaretGeometryTests
{
    private static readonly ScreenRect Field = new(100, 200, 300, 30);
    private static readonly (int X, int Y) Cursor = (900, 700);

    [Fact]
    public void ACaretInsideTheFieldWins()
    {
        var caret = new ScreenRect(150, 204, 2, 22);
        Assert.Equal(caret, CaretGeometry.Choose(caret, Field, Cursor));
    }

    [Fact]
    public void ACaretElsewhereIsIgnoredForTheField()
    {
        var stale = new ScreenRect(700, 50, 2, 22);
        Assert.Equal(Field, CaretGeometry.Choose(stale, Field, Cursor));
    }

    [Fact]
    public void ACaretAtTheFieldEdgeStillCounts()
    {
        // Tall carets reach past the field's border; the middle is what decides.
        var caret = new ScreenRect(399, 196, 1, 36);
        Assert.Equal(caret, CaretGeometry.Choose(caret, Field, Cursor));
        var outside = new ScreenRect(406, 204, 1, 22);
        Assert.Equal(Field, CaretGeometry.Choose(outside, Field, Cursor));
    }

    [Fact]
    public void WithoutACaretTheFieldIsUsed()
    {
        Assert.Equal(Field, CaretGeometry.Choose(null, Field, Cursor));
    }

    [Fact]
    public void WithoutAFieldTheCaretIsUsed()
    {
        var caret = new ScreenRect(10, 10, 0, 18);
        Assert.Equal(new ScreenRect(10, 10, 1, 18), CaretGeometry.Choose(caret, null, Cursor));
        Assert.Equal(new ScreenRect(10, 10, 1, 18), CaretGeometry.Choose(caret, new ScreenRect(5, 5, 0, 0), Cursor));
    }

    [Fact]
    public void WithNothingElseTheMousePointerIsUsed()
    {
        Assert.Equal(ScreenRect.FromPoint(900, 700), CaretGeometry.Choose(null, null, Cursor));
    }

    [Fact]
    public void ZeroWidthCaretsBecomeOnePixelWide()
    {
        var caret = new ScreenRect(150, 204, 0, 22);
        Assert.Equal(new ScreenRect(150, 204, 1, 22), CaretGeometry.Choose(caret, Field, Cursor));
    }

    [Fact]
    public void CornersInAnyOrderMakeAValidRectangle()
    {
        Assert.Equal(ScreenRect.FromLTRB(10, 20, 30, 40), CaretGeometry.FromCorners(30, 40, 10, 20));
        Assert.Equal(ScreenRect.FromLTRB(-50, -20, -48, 0), CaretGeometry.FromCorners(-50, -20, -48, 0));
    }
}

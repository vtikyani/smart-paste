using SmartPaste.App.Services;
using SmartPaste.App.UI.Popups;
using SmartPaste.App.UI.Tray;

// Where the flyout and the popups land, in physical pixels, for every taskbar position and screen edge.
public sealed class PlacementTests
{
    // A 1920 x 1080 monitor with a 48 px taskbar along the bottom.
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);
    private static readonly ScreenRect WorkArea = new(0, 0, 1920, 1032);
    private static readonly TaskbarPosition BottomBar = new(TaskbarEdge.Bottom, new ScreenRect(0, 1032, 1920, 48));

    [Fact]
    public void FlyoutSitsAboveABottomTaskbarCenteredOnTheIcon()
    {
        var icon = new ScreenRect(1500, 1040, 32, 32);
        var (x, y) = FlyoutPlacement.Place(360, 500, icon, WorkArea, BottomBar, margin: 12);
        Assert.Equal(1516 - 180, x);
        Assert.Equal(1032 - 12 - 500, y);
    }

    [Fact]
    public void FlyoutKeepsItsMarginFromTheScreenEdge()
    {
        var icon = new ScreenRect(1880, 1040, 32, 32);
        var (x, _) = FlyoutPlacement.Place(360, 500, icon, WorkArea, BottomBar, margin: 12);
        Assert.Equal(1920 - 12 - 360, x);
    }

    [Fact]
    public void FlyoutOpensBelowATopTaskbar()
    {
        var bar = new TaskbarPosition(TaskbarEdge.Top, new ScreenRect(0, 0, 1920, 48));
        var work = new ScreenRect(0, 48, 1920, 1032);
        var (x, y) = FlyoutPlacement.Place(360, 500, new ScreenRect(1500, 8, 32, 32), work, bar, margin: 12);
        Assert.Equal(1336, x);
        Assert.Equal(48 + 12, y);
    }

    [Fact]
    public void FlyoutOpensBesideASideTaskbar()
    {
        var left = new TaskbarPosition(TaskbarEdge.Left, new ScreenRect(0, 0, 62, 1080));
        var (lx, ly) = FlyoutPlacement.Place(360, 500, new ScreenRect(15, 900, 32, 32), new ScreenRect(62, 0, 1858, 1080), left, margin: 12);
        Assert.Equal(62 + 12, lx);
        Assert.Equal(1080 - 12 - 500, ly);

        var right = new TaskbarPosition(TaskbarEdge.Right, new ScreenRect(1858, 0, 62, 1080));
        var (rx, ry) = FlyoutPlacement.Place(360, 500, new ScreenRect(1873, 300, 32, 32), new ScreenRect(0, 0, 1858, 1080), right, margin: 12);
        Assert.Equal(1858 - 12 - 360, rx);
        Assert.Equal(316 - 250, ry);
    }

    [Fact]
    public void FlyoutStaysClearOfAnAutoHiddenTaskbar()
    {
        // An auto-hidden taskbar is not part of the work area, but it is on screen while its icon is clicked.
        var (_, y) = FlyoutPlacement.Place(360, 500, new ScreenRect(1500, 1040, 32, 32), Monitor, BottomBar, margin: 12);
        Assert.Equal(1032 - 12 - 500, y);
        Assert.Equal(1032 - 24, FlyoutPlacement.MaxHeight(Monitor, BottomBar, margin: 12));
    }

    [Fact]
    public void FlyoutLargerThanTheScreenKeepsItsTopLeftVisible()
    {
        var (x, y) = FlyoutPlacement.Place(4000, 3000, new ScreenRect(1500, 1040, 32, 32), WorkArea, BottomBar, margin: 12);
        Assert.Equal(12, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void FlyoutWithoutTaskbarInformationBehavesAsBottomTaskbar()
    {
        var (x, y) = FlyoutPlacement.Place(360, 500, ScreenRect.FromPoint(1700, 1050), WorkArea, null, margin: 12);
        Assert.Equal(1700 - 180, x);
        Assert.Equal(1032 - 12 - 500, y);
    }

    [Fact]
    public void PopupGoesBelowTheFieldWhenThereIsRoom()
    {
        var field = new ScreenRect(300, 400, 280, 32);
        Assert.Equal((300, 438), PopupPlacement.BelowOrAbove(field, 320, 200, WorkArea, gap: 6));
    }

    [Fact]
    public void PopupFlipsAboveTheFieldNearTheBottom()
    {
        var field = new ScreenRect(300, 960, 280, 32);
        Assert.Equal((300, 960 - 6 - 200), PopupPlacement.BelowOrAbove(field, 320, 200, WorkArea, gap: 6));
    }

    [Fact]
    public void PopupWithNoRoomEitherSideUsesTheRoomierSideAndStaysOnScreen()
    {
        var tall = new ScreenRect(300, 100, 280, 700);
        var (_, y) = PopupPlacement.BelowOrAbove(tall, 320, 400, WorkArea, gap: 6);
        Assert.Equal(1032 - 400, y);
    }

    [Fact]
    public void PopupIsPulledInFromTheRightEdge()
    {
        var field = new ScreenRect(1800, 400, 100, 32);
        Assert.Equal(1920 - 320, PopupPlacement.BelowOrAbove(field, 320, 200, WorkArea, gap: 6).X);
    }

    [Fact]
    public void FieldHintSitsAboveTheFieldsTopRightCorner()
    {
        var field = new ScreenRect(300, 400, 280, 32);
        Assert.Equal((300 + 280 - 150, 400 - 4 - 30), PopupPlacement.FieldHint(field, 150, 30, WorkArea, gap: 4));
    }

    [Fact]
    public void FieldHintGoesBelowAFieldAtTheTopOfTheScreen()
    {
        var field = new ScreenRect(300, 10, 280, 32);
        Assert.Equal(42 + 4, PopupPlacement.FieldHint(field, 150, 30, WorkArea, gap: 4).Y);
    }
}

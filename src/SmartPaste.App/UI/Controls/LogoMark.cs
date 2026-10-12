using System.Windows;
using System.Windows.Media;
using SmartPaste.App.UI.Brand;

namespace SmartPaste.App.UI.Controls;

// The Smart Paste mark, drawn hinted for the exact number of device pixels it covers, so it stays crisp at
// small sizes and any display scale (a scaled vector image goes soft below about 48 px). Monochrome draws the
// glyph in Foreground instead of the full-color mark.
public sealed class LogoMark : FrameworkElement
{
    public static readonly DependencyProperty MonochromeProperty = DependencyProperty.Register(
        nameof(Monochrome), typeof(bool), typeof(LogoMark),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(LogoMark),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    private (int Pixels, bool Monochrome, object Shape)? _cached;

    public LogoMark()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    public bool Monochrome
    {
        get => (bool)GetValue(MonochromeProperty);
        set => SetValue(MonochromeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square: the explicit size if there is one, else whatever fits.
        var side = double.IsNaN(Width) ? Math.Min(availableSize.Width, availableSize.Height) : Width;
        if (double.IsInfinity(side)) side = 32;
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var side = Math.Min(ActualWidth, ActualHeight);
        var pixels = (int)Math.Round(side * scale);
        if (pixels < 16) return;

        if (_cached is not { } cached || cached.Pixels != pixels || cached.Monochrome != Monochrome)
        {
            cached = (pixels, Monochrome, Monochrome ? BrandArt.CreateGlyph(pixels) : BrandArt.CreateLogo(pixels));
            _cached = cached;
        }

        dc.PushTransform(new ScaleTransform(1 / scale, 1 / scale));
        if (cached.Shape is Geometry glyph) dc.DrawGeometry(Foreground, null, glyph);
        else dc.DrawDrawing((Drawing)cached.Shape);
        dc.Pop();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();
}

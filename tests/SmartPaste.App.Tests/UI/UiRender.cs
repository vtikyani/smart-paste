using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Renders WPF elements to bitmaps without showing any window on screen.
internal static class UiRender
{
    // Lays the element out (at a fixed width when given) and renders it at a display scale. The element is told
    // that scale, so DPI-aware drawing (the hinted logo) renders for the real pixel size, as it would on screen.
    public static BitmapSource Element(FrameworkElement element, double scale = 1, double? width = null)
    {
        VisualTreeHelper.SetRootDpi(element, new DpiScale(scale, scale));
        element.Measure(new Size(width ?? double.PositiveInfinity, double.PositiveInfinity));
        var size = new Size(width ?? element.DesiredSize.Width, element.DesiredSize.Height);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    // Runs the UI thread's dispatcher for a while (animations, timers, bindings), then returns. UI thread only.
    public static void Settle(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // Takes a window's content out of the (never shown) window so it can be laid out and rendered on its own.
    public static FrameworkElement Detach(Window window)
    {
        var root = (FrameworkElement)window.Content;
        var dataContext = window.DataContext;
        window.Content = null;
        root.DataContext = dataContext;
        root.Opacity = 1;
        return root;
    }

    // Wraps content in a panel shaped like a Windows 11 flyout: rounded, hairline edge, soft shadow, with the
    // backdrop's solid fallback color standing in for acrylic (which only exists on screen).
    public static FrameworkElement FlyoutFrame(FrameworkElement content, bool dark)
    {
        var fill = new SolidColorBrush(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        var grid = new Grid { Margin = new Thickness(36, 28, 36, 48) };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = fill,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 40, ShadowDepth = 12, Direction = 270, Color = Colors.Black, Opacity = dark ? 0.5 : 0.22,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
            }
        });
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = fill,
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = content
        });
        return grid;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // Straight BGRA pixels, row by row.
    public static byte[] Pixels(BitmapSource bitmap)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return pixels;
    }

    // How many pixels have any opacity at all.
    public static int VisiblePixels(BitmapSource bitmap)
    {
        var pixels = Pixels(bitmap);
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 0) count++;
        return count;
    }
}

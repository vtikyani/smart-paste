using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartPaste.App.UI.Controls;

// A highlighter stroke that sweeps out and back with the caret riding its end: the brand's "working on it" mark.
// It animates only while visible, and holds still when Windows animations are turned off.
public sealed class ThinkingMark : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ThinkingMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaretBrushProperty = DependencyProperty.Register(
        nameof(CaretBrush), typeof(Brush), typeof(ThinkingMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(ThinkingMark),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public ThinkingMark()
    {
        Width = 22;
        Height = 12;
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    private void UpdateAnimation()
    {
        if (IsVisible && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(ProgressProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else
        {
            BeginAnimation(ProgressProperty, null);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var progress = (double)GetValue(ProgressProperty);
        var caretWidth = Math.Max(1.5, ActualHeight / 8);
        var length = Math.Max(2, (ActualWidth - caretWidth - 1) * progress);
        var barHeight = Math.Round(ActualHeight * 0.62);
        var top = (ActualHeight - barHeight) / 2;
        var slant = barHeight * 0.2;

        if (Fill is not null)
        {
            var bar = new StreamGeometry();
            using (var g = bar.Open())
            {
                g.BeginFigure(new Point(slant, top), true, true);
                g.LineTo(new Point(length, top), true, false);
                g.LineTo(new Point(length - slant, top + barHeight), true, false);
                g.LineTo(new Point(0, top + barHeight), true, false);
            }
            bar.Freeze();
            dc.DrawGeometry(Fill, null, bar);
        }
        if (CaretBrush is not null)
            dc.DrawRectangle(CaretBrush, null, new Rect(length + 1, 0, caretWidth, ActualHeight));
    }
}

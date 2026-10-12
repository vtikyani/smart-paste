using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartPaste.App.UI.Controls;

// A soft, rounded mark behind its content, like selected text. Smart Paste uses it for the one value that matters
// in a view: the armed field count, the value Ctrl+V will paste.
public sealed class HighlighterMark : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(HighlighterMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(HighlighterMark),
        new FrameworkPropertyMetadata(new Thickness(5, 1, 5, 1), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    // Room between the content and the edges of the stroke.
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding;
        if (Child is null) return new Size(p.Left + p.Right, p.Top + p.Bottom);
        Child.Measure(new Size(
            Math.Max(0, constraint.Width - p.Left - p.Right),
            Math.Max(0, constraint.Height - p.Top - p.Bottom)));
        return new Size(Child.DesiredSize.Width + p.Left + p.Right, Child.DesiredSize.Height + p.Top + p.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var p = Padding;
        Child?.Arrange(new Rect(p.Left, p.Top,
            Math.Max(0, arrangeSize.Width - p.Left - p.Right),
            Math.Max(0, arrangeSize.Height - p.Top - p.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Fill is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var radius = Math.Min(4, ActualHeight / 2);
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
    }
}

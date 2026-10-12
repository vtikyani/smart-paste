using System.Windows;
using System.Windows.Controls;

namespace SmartPaste.App.UI.Controls;

// Rounds a Border into a true pill (ends are half circles) at whatever size it ends up. A large fixed radius does not
// work in WPF: it shares oversized radii out per side, which turns the border into an ellipse.
public static class Pill
{
    public static readonly DependencyProperty IsPillProperty = DependencyProperty.RegisterAttached(
        "IsPill", typeof(bool), typeof(Pill), new PropertyMetadata(false, OnIsPillChanged));

    public static bool GetIsPill(Border border) => (bool)border.GetValue(IsPillProperty);

    public static void SetIsPill(Border border, bool value) => border.SetValue(IsPillProperty, value);

    private static void OnIsPillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        border.SizeChanged -= OnSizeChanged;
        if (e.NewValue is not true) return;
        border.SizeChanged += OnSizeChanged;
        Round(border);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Round((Border)sender);

    private static void Round(Border border)
    {
        // Before layout, an explicit Height is the best guess; ActualHeight takes over once arranged.
        var height = border.ActualHeight > 0 ? border.ActualHeight : border.Height;
        if (double.IsNaN(height) || height <= 0) return;
        var width = border.ActualWidth > 0 ? border.ActualWidth : double.PositiveInfinity;
        border.CornerRadius = new CornerRadius(Math.Min(height, width) / 2);
    }
}

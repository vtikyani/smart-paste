using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartPaste.App.UI.Onboarding;

internal partial class PracticeField : UserControl
{
    private static readonly Duration Sweep = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(450));
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(950);

    private PracticeFieldViewModel? _field;

    public PracticeField()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Track(DataContext as PracticeFieldViewModel);
        Unloaded += (_, _) => Track(null);
        Loaded += (_, _) => Track(DataContext as PracticeFieldViewModel);
    }

    // The text box itself, so the practice step can tell which field a paste is for.
    public TextBox Box => Input;

    // The highlighter: sweep a marker over the new value, hold, then fade to the wash that stays.
    public void PlayHighlighter()
    {
        Input.ApplyTemplate();
        if (Input.Template.FindName("Marker", Input) is not Border marker) return;

        marker.Width = Math.Max(24, Math.Min(TextWidth(Input.Text) + 12, Input.ActualWidth - 10));
        var scale = new ScaleTransform();
        marker.RenderTransform = scale;
        // The template keeps the marker hidden; it's down (visible) from now until the fade below runs.
        marker.Opacity = 1;

        // Ink on yellow while the marker is down, then back to the theme's text color.
        var ink = ColorOf("Brand.HighlightTextBrush", Colors.Black);
        var normal = ColorOf("TextFillColorPrimaryBrush", ink);
        var foreground = new SolidColorBrush(ink);
        Input.Foreground = foreground;

        if (!Motion.IsReduced)
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, Sweep) { EasingFunction = Motion.EaseOut });

        var fade = new DoubleAnimation(1, 0, Fade) { BeginTime = Hold, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) => Input.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        marker.BeginAnimation(OpacityProperty, fade);
        foreground.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(normal, Fade) { BeginTime = Hold });
    }

    private void Track(PracticeFieldViewModel? field)
    {
        if (ReferenceEquals(field, _field)) return;
        if (_field is not null) _field.PropertyChanged -= OnFieldChanged;
        _field = field;
        if (_field is not null) _field.PropertyChanged += OnFieldChanged;
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PracticeFieldViewModel.FillCount)) PlayHighlighter();
    }

    private double TextWidth(string text)
    {
        if (text.Length == 0) return 0;
        var typeface = new Typeface(Input.FontFamily, Input.FontStyle, Input.FontWeight, Input.FontStretch);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, Input.FlowDirection, typeface, Input.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private Color ColorOf(string key, Color fallback) =>
        TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
}

using System.Windows;
using System.Windows.Controls;

namespace SmartPaste.App.UI.Controls;

// One keyboard key drawn as a small key cap, for shortcuts like Ctrl + V. Styled in UI/Theme/Controls.xaml.
public sealed class KeyCap : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(KeyCap), new FrameworkPropertyMetadata(""));

    static KeyCap()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(KeyCap), new FrameworkPropertyMetadata(typeof(KeyCap)));
        FocusableProperty.OverrideMetadata(typeof(KeyCap), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

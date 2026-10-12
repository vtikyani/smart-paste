using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartPaste.App.UI.Settings;

// Visible when the value is false: for empty states and "nothing to show" messages.
[ValueConversion(typeof(bool), typeof(Visibility))]
internal sealed class FalseToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Visible when the value is a non-empty string: for messages that are blank when there's nothing to say.
[ValueConversion(typeof(string), typeof(Visibility))]
internal sealed class TextToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

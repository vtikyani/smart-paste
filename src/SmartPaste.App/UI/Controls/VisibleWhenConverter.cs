using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartPaste.App.UI.Controls;

// Visible when the value is "something": true, a non-empty string or collection, or any other non-null value.
// ConverterParameter=Not flips it.
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            _ => true
        };
        if (parameter is string p && p.Equals("Not", StringComparison.OrdinalIgnoreCase)) present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

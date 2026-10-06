using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AionMeter.App.Controls;

/// <summary>true → Visible. Pass ConverterParameter="!" to invert.</summary>
public sealed class VisibleIf : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "!") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

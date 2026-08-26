using System.Globalization;

namespace Strunika.Mobile.Converters;

/// <summary>Boolean negation for IsVisible bindings.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

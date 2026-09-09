using System.Globalization;

namespace Strunika.Mobile.Converters;

/// <summary>
/// A bool as an opacity: full when true, faded when false. For a control that
/// stays where it is and stays tappable, but has nothing to act on — an
/// editor button with no chord under the playhead. Hiding it instead would
/// move everything beside it.
/// </summary>
public sealed class FadedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.35;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

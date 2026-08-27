using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Controls.Xaml;

namespace Strunika.Mobile.Theme;

/// <summary>
/// <c>WidthRequest="{t:Size 60}"</c> — a live binding to <see cref="Metrics"/>,
/// so the value follows the screen class the way <c>{t:Theme}</c> follows the
/// theme. <c>Min</c> keeps touch targets honest (<c>{t:Size 44, Min=44}</c>);
/// <c>Hero=True</c> uses the hero factor for content that may grow on a tablet.
/// </summary>
[ContentProperty(nameof(Value))]
public sealed class SizeExtension : IMarkupExtension<BindingBase>
{
    public double Value { get; set; }
    public double Min { get; set; }
    public bool Hero { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(Hero ? nameof(Metrics.HeroScale) : nameof(Metrics.Scale), BindingMode.OneWay, new ScaleConverter(Value, Min), source: Metrics.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);

    private sealed class ScaleConverter(double value, double min) : IValueConverter
    {
        public object Convert(object? scale, Type targetType, object? parameter, CultureInfo culture) =>
            Math.Max(min, value * (scale is double s ? s : 1.0));

        public object ConvertBack(object? v, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>
/// <c>StrokeShape="{t:Round 60}"</c> — the pill/circle for a control whose
/// size is <c>{t:Size 60}</c>: the radius scales with it, so a round button
/// stays round on a compact phone.
/// </summary>
[ContentProperty(nameof(Size))]
public sealed class RoundExtension : IMarkupExtension<BindingBase>
{
    public double Size { get; set; }
    public double Min { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Metrics.Scale), BindingMode.OneWay, new RoundConverter(Size, Min), source: Metrics.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);

    private sealed class RoundConverter(double size, double min) : IValueConverter
    {
        public object Convert(object? scale, Type targetType, object? parameter, CultureInfo culture) =>
            new RoundRectangle { CornerRadius = new CornerRadius(Math.Max(min, size * (scale is double s ? s : 1.0)) / 2) };

        public object ConvertBack(object? v, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>
/// <c>Margin="{t:ContentInset}"</c> on a content column (it keeps
/// <c>HorizontalOptions="Fill"</c>): on a tablet the column is 560 pt wide and
/// centred, on phones the margin is zero. Never centre such a column with
/// <c>HorizontalOptions="Center"</c> — it would take its natural width and
/// overflow a narrow screen.
/// </summary>
public sealed class ContentInsetExtension : IMarkupExtension<BindingBase>
{
    /// <summary>Add the usual 20 pt page inset (cards that carried their own margin).</summary>
    public bool Plus { get; set; }
    /// <summary>Vertical margins to keep alongside the side inset.</summary>
    public double Top { get; set; }
    public double Bottom { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(Plus ? nameof(Metrics.ContentInsetPlus) : nameof(Metrics.ContentInset), BindingMode.OneWay,
                    Top > 0 || Bottom > 0 ? new VerticalConverter(Top, Bottom) : null, source: Metrics.Instance);

    private sealed class VerticalConverter(double top, double bottom) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Thickness t ? new Thickness(t.Left, top, t.Right, bottom) : new Thickness(0, top, 0, bottom);

        public object ConvertBack(object? v, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Controls;

/// <summary>
/// " Pro" + the small string-wave, appended to a page title when the user
/// has Strunika Pro (user request 2026-08-27). The wave keeps the same
/// ratio to the font as in Settings (54×20 at 22 pt).
/// </summary>
public sealed class ProSuffix : HorizontalStackLayout
{
    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(ProSuffix), 34.0, propertyChanged: (b, _, _) => ((ProSuffix)b).Resize());

    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    private readonly Label _label;
    private readonly WaveMark _wave;

    public ProSuffix()
    {
        Spacing = 0;
        VerticalOptions = LayoutOptions.End;
        _label = new Label
        {
            Text = "Pro",
            FontFamily = "Display",
            VerticalOptions = LayoutOptions.End,
        };
        _label.SetAppThemeColor(Label.TextColorProperty, Tokens.Light("AccentText"), Tokens.Dark("AccentText"));
        _wave = new WaveMark { VerticalOptions = LayoutOptions.Center };
        _wave.SetAppThemeColor(WaveMark.ColorProperty, Tokens.Light("Accent"), Tokens.Dark("Accent"));
        _wave.SetAppThemeColor(WaveMark.GlowColorProperty, Tokens.Light("Glow"), Tokens.Dark("Glow"));
        Add(_label);
        Add(_wave);
        Resize();
    }

    private void Resize()
    {
        double size = FontSize;
        _label.FontSize = size;
        _label.Margin = new Thickness(size * 0.28, 0, 0, 0);
        _wave.WidthRequest = size * 54 / 22;
        _wave.HeightRequest = size * 20 / 22;
        // Measured against Vollkorn: with these margins the string sits on the
        // vertical centre of the "o" (user rule 2026-08-27).
        _wave.Margin = new Thickness(size * 0.3, size * 0.22, 0, 0);
    }
}

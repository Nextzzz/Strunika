using Microsoft.Maui.Controls.Xaml;

namespace Strunika.Mobile.Theme;

/// <summary>
/// <c>TextColor="{t:Theme TextSec}"</c> — an AppThemeBinding built from
/// <see cref="Tokens"/>, so every themed colour is written once and follows
/// light/dark switches live. <c>{t:Theme Glow, Brush=True}</c> yields
/// SolidColorBrush values for Brush-typed properties (Shadow.Brush).
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class ThemeExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = "";

    public bool Brush { get; set; }

    // XAML only: AppThemeBindingExtension needs the real XAML service provider.
    // From code-behind use Tokens.Current(key) or IconView.ThemeKey instead.
    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var light = Tokens.Light(Key);
        var dark = Tokens.Dark(Key);
        var ext = Brush
            ? new AppThemeBindingExtension { Light = new SolidColorBrush(light), Dark = new SolidColorBrush(dark) }
            : new AppThemeBindingExtension { Light = light, Dark = dark };
        return ((IMarkupExtension<BindingBase>)ext).ProvideValue(serviceProvider);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

using Microsoft.Maui.Controls.Shapes;
using Strunika.Mobile.Services;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The page background dissolving downwards over its own height, laid over the
/// top of a list that sits under a header (the Songs tab, the chord dictionary).
/// It is not there while the list is at its top and fades in over the first
/// shade-height of scrolling (<see cref="Follow"/>), the way the hairline under
/// a large title does on iOS, so the first row at rest is untouched and a row
/// scrolling out dissolves under the header instead of being cut by it.
/// Put it in the list's row with <c>HeightRequest="{t:Size 28}"</c>, declared
/// after the list so it draws on top, and feed it the list's scroll offset.
/// </summary>
public sealed class ScrollShade : Border
{
    private double _shade = -1;

    public ScrollShade()
    {
        InputTransparent = true;
        StrokeThickness = 0;
        StrokeShape = new Rectangle();
        BackgroundColor = Colors.Transparent;
        VerticalOptions = LayoutOptions.Start;
        Opacity = 0;
        Paint();
        // The page background follows the theme; a page that is pushed and
        // popped must not leave these subscriptions behind (Unloaded).
        Loaded += (_, _) =>
        {
            Paint();
            AppSettings.Changed += OnSettingsChanged;
            if (Application.Current != null) Application.Current.RequestedThemeChanged += OnThemeChanged;
        };
        Unloaded += (_, _) =>
        {
            AppSettings.Changed -= OnSettingsChanged;
            if (Application.Current != null) Application.Current.RequestedThemeChanged -= OnThemeChanged;
        };
    }

    /// <summary>Where the list is: 0 at its top. One native write per visible
    /// step of the fade, not one per scroll event.</summary>
    public void Follow(double offset)
    {
        double depth = Math.Max(1, Height > 0 ? Height : Metrics.Instance.Size(28));
        double shade = Math.Clamp(offset / depth, 0, 1);
        if (Math.Abs(shade - _shade) < 0.01) return;
        _shade = shade;
        Opacity = shade;
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == nameof(AppSettings.Theme)) Paint();
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Paint();

    private void Paint()
    {
        var bg = Tokens.Current("Bg");
        Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(bg, 0f),
                new GradientStop(bg.WithAlpha(0.7f), 0.4f),
                new GradientStop(bg.WithAlpha(0f), 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }
}

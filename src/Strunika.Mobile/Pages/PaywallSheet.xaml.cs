using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Pro sheet. Opened from a locked control (with that feature called out)
/// or from Settings (full list). Prices and purchase come in M6 — until
/// then the plan cards are placeholders and "Continue" is disabled.
/// </summary>
public partial class PaywallSheet : ContentPage
{
    private static bool _open;
    private readonly bool _pushed;

    public PaywallSheet(Feature? feature, bool pushed = false)
    {
        InitializeComponent();
        _pushed = pushed;
        ApplyShade();
        AppSettings.Changed += OnSettingsChanged;
        if (Application.Current != null) Application.Current.RequestedThemeChanged += OnThemeChanged;

        if (feature is { } f)
        {
            FeatureCallout.IsVisible = true;
            FeatureName.Text = Loc.Get("Feature_" + f);
        }

        // Product order (user decision 2026-08-27): the six headline features
        // first, the rest in declaration order.
        Feature[] headline = { Feature.UnlimitedSongs, Feature.ChordEditor, Feature.Export, Feature.AltTunings, Feature.FullChordVocabulary, Feature.TransposeCapo };
        foreach (Feature each in headline.Concat(Enum.GetValues<Feature>().Except(headline)))
        {
            var row = new HorizontalStackLayout { Spacing = 10 };
            row.Add(new IconView { Name = "check", Size = 18, StrokeSize = 2.6, ThemeKey = "AccentText", VerticalOptions = LayoutOptions.Center });
            row.Add(new Label { Text = Loc.Get("Feature_" + each), FontSize = 15, VerticalOptions = LayoutOptions.Center });
            FeatureList.Add(row);
        }
    }

    /// <param name="feature">The locked feature to call out, or null for the full sheet.</param>
    /// <param name="push">true = slide in from the right edge as a pushed page (Settings →
    /// "Learn more", user request 2026-08-26); false = modal sheet from a locked control.</param>
    public static async Task ShowAsync(Feature? feature, bool push = false)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        Strunika.Core.Diagnostics.FileLog.Info($"Paywall: open for {feature?.ToString() ?? "all"} on {host?.GetType().Name ?? "null"}");
        if (host == null) return;
        _open = true;
        try
        {
            var page = new PaywallSheet(feature, push);
            if (push) await host.Navigation.PushAsync(page, animated: true);
            else await host.Navigation.PushModalAsync(page, animated: true);
        }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("Paywall failed", ex); }
        finally { _open = false; }
    }

    /// <summary>Same idea as the shade under the tab bar (RootPage): the page
    /// background, opaque behind the title and dissolving over the last third,
    /// so scrolled content fades out under the header instead of being cut.</summary>
    private void ApplyShade()
    {
        var bg = Theme.Tokens.Current("Bg");
        HeaderShade.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(bg, 0f),
                new GradientStop(bg, 0.62f),
                new GradientStop(bg.WithAlpha(0.85f), 0.78f),
                new GradientStop(bg.WithAlpha(0.45f), 0.9f),
                new GradientStop(bg.WithAlpha(0f), 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == nameof(AppSettings.Theme)) ApplyShade();
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => ApplyShade();

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        AppSettings.Changed -= OnSettingsChanged;
        if (Application.Current != null) Application.Current.RequestedThemeChanged -= OnThemeChanged;
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_pushed) await Navigation.PopAsync(animated: true);
        else await Navigation.PopModalAsync(animated: true);
    }
}

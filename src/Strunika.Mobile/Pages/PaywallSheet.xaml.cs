using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pro;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Pro sheet. Opened from a locked control (with that feature called out)
/// or from Settings (full list). Prices and purchase come in M6 — until
/// then the plan cards are placeholders and "Continue" is disabled.
/// </summary>
public partial class PaywallSheet : ContentPage
{
    private static bool _open;

    public PaywallSheet(Feature? feature)
    {
        InitializeComponent();

        if (feature is { } f)
        {
            FeatureCallout.IsVisible = true;
            FeatureName.Text = Loc.Get("Feature_" + f);
        }

        foreach (Feature each in Enum.GetValues<Feature>())
        {
            var row = new HorizontalStackLayout { Spacing = 10 };
            row.Add(new IconView { Name = "check", Size = 18, ThemeKey = "AccentText", VerticalOptions = LayoutOptions.Center });
            row.Add(new Label { Text = Loc.Get("Feature_" + each), FontSize = 15, VerticalOptions = LayoutOptions.Center });
            FeatureList.Add(row);
        }
    }

    public static async Task ShowAsync(Feature? feature)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        Strunika.Core.Diagnostics.FileLog.Info($"Paywall: open for {feature?.ToString() ?? "all"} on {host?.GetType().Name ?? "null"}");
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new PaywallSheet(feature), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("Paywall failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PopModalAsync(animated: true);
}

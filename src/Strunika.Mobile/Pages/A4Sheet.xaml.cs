using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Pages;

/// <summary>A4 reference pitch, 430–450 Hz (Pro). Saved as you go.</summary>
public partial class A4Sheet : ContentPage
{
    private static bool _open;

    public A4Sheet()
    {
        InitializeComponent();
        A4Slider.Value = AppSettings.A4Reference - A4Min;
        Render();
    }

    private void Render() => Value.Text = $"{AppSettings.A4Reference:0} {Loc.Get("Unit_Hz")}";

    private void Set(double hz)
    {
        hz = Math.Clamp(Math.Round(hz), 430, 450);
        if (Math.Abs(hz - AppSettings.A4Reference) < 0.5) return;
        AppSettings.A4Reference = hz;
        if (Math.Abs(A4Slider.Value + A4Min - hz) > 0.5) A4Slider.Position = hz - A4Min;
        Haptics.Default.Selection();
        Render();
    }

    private const double A4Min = 430;

    // The bar runs 0–20 for 430–450 Hz; a drag rounds to whole hertz as it goes.
    private void OnSliderDragging(object? sender, double v) => Set(Math.Round(A4Min + v));
    private void OnSliderDone(object? sender, double v) { Set(Math.Round(A4Min + v)); A4Slider.Position = AppSettings.A4Reference - A4Min; }
    private void OnMinus(object? sender, TappedEventArgs e) => Set(AppSettings.A4Reference - 1);
    private void OnPlus(object? sender, TappedEventArgs e) => Set(AppSettings.A4Reference + 1);
    private void OnReset(object? sender, TappedEventArgs e) => Set(440);

    public static async Task ShowAsync()
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new A4Sheet(), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("A4Sheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync(animated: true);
}

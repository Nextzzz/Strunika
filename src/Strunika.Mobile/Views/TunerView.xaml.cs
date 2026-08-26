using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class TunerView : ContentView
{
    public TunerView()
    {
        InitializeComponent();
    }

    private void OnA4Tapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is TunerViewModel { A4Locked: true })
            _ = PaywallSheet.ShowAsync(Feature.A4Reference);
        // M1: A4 reference picker for Pro users.
    }
}

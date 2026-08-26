using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class LiveView : ContentView
{
    public LiveView()
    {
        InitializeComponent();
        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is LiveViewModel vm)
                vm.ProRequired += OnProRequired;
        };
    }

    private void OnProRequired(object? sender, Feature feature) =>
        _ = PaywallSheet.ShowAsync(feature);
}

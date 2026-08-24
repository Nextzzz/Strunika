using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage(TunerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

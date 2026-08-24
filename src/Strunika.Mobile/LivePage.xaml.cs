using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile;

public partial class LivePage : ContentPage
{
    public LivePage(LiveViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

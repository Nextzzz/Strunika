using Strunika.Mobile.Localization;

namespace Strunika.Mobile.Views;

public partial class LibraryView : ContentView
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private async void OnAddTapped(object? sender, TappedEventArgs e)
    {
        // M2 brings the add-song sheet (file / recording / YouTube).
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
            await page.DisplayAlert(Loc.Get("Library_Add"), Loc.Get("Common_Soon"), "OK");
    }
}

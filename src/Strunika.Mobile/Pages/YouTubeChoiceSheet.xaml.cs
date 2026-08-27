namespace Strunika.Mobile.Pages;

/// <summary>
/// Small choice when the clipboard already holds a YouTube link: add that
/// link, or open the built-in YouTube to search by hand. The link option is
/// the gold one — it is the likely intent.
/// </summary>
public partial class YouTubeChoiceSheet : ContentPage
{
    private static bool _open;
    private readonly Func<bool, Task> _onPick;
    private bool _picked;

    public YouTubeChoiceSheet(string link, Func<bool, Task> onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        LinkLabel.Text = link.Replace("https://", "").Replace("http://", "").Replace("www.", "");
    }

    /// <param name="onPick">true = use the clipboard link, false = open the browser.</param>
    public static async Task ShowAsync(string link, Func<bool, Task> onPick)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new YouTubeChoiceSheet(link, onPick), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("YouTubeChoiceSheet failed", ex); }
        finally { _open = false; }
    }

    private async Task PickAsync(bool useLink)
    {
        if (_picked) return;
        _picked = true;
        await Navigation.PopModalAsync(animated: true);
        await _onPick(useLink);
    }

    private async void OnUseLinkTapped(object? sender, TappedEventArgs e) => await PickAsync(true);

    private async void OnBrowseTapped(object? sender, TappedEventArgs e) => await PickAsync(false);

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_picked) return;
        _picked = true;
        await Navigation.PopModalAsync(animated: true);
    }
}

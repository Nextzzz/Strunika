using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Built-in YouTube: the user searches and opens a video like in the app,
/// and an "Add to library" bar appears as soon as a /watch page is open.
/// YouTube is a single-page app, so besides <c>Navigated</c> the page polls
/// <c>location.href</c> — pushState navigations never raise an event.
/// The video itself is added through the normal YouTube path (metadata +
/// audio extraction); nothing is scraped from the page except its title.
/// </summary>
public partial class YouTubeBrowserPage : ContentPage
{
    private static bool _open;
    private readonly LibraryViewModel _vm;
    private readonly IYouTubeSource _youtube;
    private readonly IDispatcherTimer _poll;
    private string? _videoId;
    private bool _busy;

    public YouTubeBrowserPage(LibraryViewModel vm, IYouTubeSource youtube)
    {
        InitializeComponent();
        _vm = vm;
        _youtube = youtube;
        _poll = Dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromMilliseconds(700);
        _poll.Tick += async (_, _) => await ProbeAsync();
    }

    public static async Task ShowAsync(LibraryViewModel vm)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        var youtube = host?.Handler?.MauiContext?.Services.GetService<IYouTubeSource>();
        if (host == null || youtube == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new YouTubeBrowserPage(vm, youtube), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("YouTubeBrowserPage failed", ex); }
        finally { _open = false; }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _poll.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _poll.Stop();
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e) => await ProbeAsync();

    private async Task ProbeAsync()
    {
        if (_busy) return;
        string? href = null, title = null;
        try
        {
            href = Unquote(await Web.EvaluateJavaScriptAsync("location.href"));
            title = Unquote(await Web.EvaluateJavaScriptAsync("document.title"));
        }
        catch { /* page still loading */ }
        var id = href == null ? null : _youtube.TryParseVideoId(href);
        if (id == _videoId)
        {
            if (id != null && !string.IsNullOrEmpty(title)) VideoTitle.Text = Clean(title);
            return;
        }
        _videoId = id;
        if (id == null)
        {
            AddBar.IsVisible = false;
            return;
        }
        VideoTitle.Text = Clean(title ?? "");
        VideoUrl.Text = $"youtu.be/{id}";
        AddBar.IsVisible = true;
    }

    /// <summary>EvaluateJavaScript returns a JSON string literal on some platforms.</summary>
    private static string? Unquote(string? s)
    {
        if (s == null || s == "null") return null;
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = System.Text.Json.JsonSerializer.Deserialize<string>(s) ?? s;
        return s;
    }

    private static string Clean(string title) =>
        title.EndsWith(" - YouTube", StringComparison.Ordinal) ? title[..^10] : title;

    private async void OnAddTapped(object? sender, EventArgs e)
    {
        if (_busy || _videoId == null) return;
        _busy = true;
        AddButton.IsEnabled = false;
        try
        {
            var error = await _vm.AddYouTubeAsync(_videoId);
            if (error == null)
            {
                Haptics.Default.Success();
                await Navigation.PopModalAsync(animated: true);
                return;
            }
            await DisplayAlert("YouTube", Loc.Get(error), "OK");
        }
        finally
        {
            _busy = false;
            AddButton.IsEnabled = true;
        }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) => await Navigation.PopModalAsync(animated: true);
}

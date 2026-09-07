using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Built-in YouTube: the user searches and opens a video like in the app,
/// and an "Add to library" bar appears as soon as a /watch page is open.
/// YouTube is a single-page app, so besides <c>Navigated</c> the address has
/// to be watched — pushState navigations never raise an event. On Windows
/// the page polls <c>location.href</c>; on iOS the poll never came back (the
/// device log had not one line from it), so there a script in the page
/// reports the address and title itself, through a WebKit message handler,
/// and nothing is asked of the page from outside. The video itself is added
/// through the normal YouTube path (metadata + audio extraction); nothing is
/// scraped from the page except its title.
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
#if IOS
        Web.HandlerChanged += (_, _) =>
        {
            if (Web.Handler?.PlatformView is not WebKit.WKWebView wk || _wired) return;
            _wired = true;
            // A link that wants a new window (target=_blank) goes nowhere in a
            // bare WKWebView — there is no second window to open. Load it here.
            wk.UIDelegate = _popups;
            // The page reports its own address: a script, injected into every
            // document, posts "href\ntitle" whenever either changes.
            var controller = wk.Configuration.UserContentController;
            controller.AddScriptMessageHandler(new AddressWatcher(OnAddress), "strunika");
            controller.AddUserScript(new WebKit.WKUserScript(new Foundation.NSString(AddressScript), WebKit.WKUserScriptInjectionTime.AtDocumentStart, true));
            // The first load may already be under way without the script: load again.
            wk.LoadRequest(new Foundation.NSUrlRequest(new Foundation.NSUrl("https://m.youtube.com/")));
            Strunika.Core.Diagnostics.FileLog.Info("youtube browser: wired");
        };
#endif
    }

#if IOS
    private readonly PopupDelegate _popups = new();
    private bool _wired;

    private const string AddressScript = """
        (function () {
          var last = '';
          function tick() {
            var now = location.href + '\n' + document.title;
            if (now === last) return;
            last = now;
            try { window.webkit.messageHandlers.strunika.postMessage(now); } catch (e) {}
          }
          setInterval(tick, 500);
          tick();
        })();
        """;

    private void OnAddress(string href, string title) =>
        MainThread.BeginInvokeOnMainThread(() => Apply(href, title));

    private sealed class AddressWatcher(Action<string, string> onAddress) : WebKit.WKScriptMessageHandler
    {
        public override void DidReceiveScriptMessage(WebKit.WKUserContentController userContentController, WebKit.WKScriptMessage message)
        {
            var body = message.Body?.ToString() ?? "";
            int cut = body.IndexOf('\n');
            if (cut < 0) onAddress(body, "");
            else onAddress(body[..cut], body[(cut + 1)..]);
        }
    }

    private sealed class PopupDelegate : WebKit.WKUIDelegate
    {
        public override WebKit.WKWebView? CreateWebView(WebKit.WKWebView webView, WebKit.WKWebViewConfiguration configuration,
                                                        WebKit.WKNavigationAction navigationAction, WebKit.WKWindowFeatures windowFeatures)
        {
            if (navigationAction.TargetFrame == null || !navigationAction.TargetFrame.MainFrame)
                webView.LoadRequest(navigationAction.Request);
            return null;
        }
    }
#endif

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
        Strunika.Core.Diagnostics.FileLog.Info("youtube browser: opened");
#if !IOS
        _poll.Start();                                          // iOS: the page reports itself
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _poll.Stop();
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        Strunika.Core.Diagnostics.FileLog.Info($"youtube browser: navigated {e.Result} {e.Url}");
#if IOS
        // The script reports; but a plain navigation carries its address here
        // too, so the bar keeps up even if the script were ever silent.
        if (!string.IsNullOrEmpty(e.Url)) Apply(e.Url, "");
#else
        await ProbeAsync();
#endif
    }

    private string? _lastHref;

    /// <summary>Windows: ask the page where it is.</summary>
    private async Task ProbeAsync()
    {
        if (_busy) return;
        string? href = null, title = null;
        try
        {
            href = Unquote(await Web.EvaluateJavaScriptAsync("location.href"));
            title = Unquote(await Web.EvaluateJavaScriptAsync("document.title"));
        }
        catch (Exception ex)
        {
            if (_lastHref != "<error>") Strunika.Core.Diagnostics.FileLog.Error("youtube browser probe", ex);
            _lastHref = "<error>";
        }
        Apply(href, title);
    }

    /// <summary>The page is at <paramref name="href"/>: show or hide the bar.</summary>
    private void Apply(string? href, string? title)
    {
        if (_busy) return;
        var id = href == null ? null : _youtube.TryParseVideoId(href);
        if (href != _lastHref)
        {
            // One line per page the person lands on: enough to see why the bar
            // did or did not appear on a device.
            Strunika.Core.Diagnostics.FileLog.Info($"youtube browser: {href ?? "<null>"} → {id ?? "no id"}");
            _lastHref = href;
        }
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
            // Said here, over the browser, where the person is — a toast on the
            // library underneath went unseen and the song seemed to vanish.
            await DisplayAlert("YouTube", Loc.Get(error), "OK");
            if (error == "Library_Duplicate") await Navigation.PopModalAsync(animated: true);
        }
        finally
        {
            _busy = false;
            AddButton.IsEnabled = true;
        }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) => await Navigation.PopModalAsync(animated: true);
}

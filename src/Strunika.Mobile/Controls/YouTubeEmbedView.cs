using System.Text.Json;
using Strunika.Core.Diagnostics;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The official YouTube player, driven through the IFrame API from a page of
/// our own (<c>Resources/Raw/player.html</c>). Nothing is downloaded or stored —
/// this is playback "like ChordAI".
/// <para>
/// The page must be served from a real https origin: navigating straight to
/// <c>youtube.com/embed/…</c> (or loading HTML with no base URL) leaves the
/// player without an origin or referrer and it answers "video player
/// configuration error 153". Windows gets the origin from a WebView2 virtual
/// host mapping, iOS from the base URL of <c>LoadHtmlString</c>.
/// </para>
/// </summary>
public sealed class YouTubeEmbedView : WebView
{
    private const string Host = "strunika.local";

    public string? VideoId { get; private set; }

    /// <summary>The player refused the video (101/150 = embedding disabled by
    /// the owner, 153 = origin problem). Raised once per load.</summary>
    public event EventHandler<int>? PlayerError;

    private bool _errorReported, _lastWant;
    private int _probesLogged, _lastState = -99;
    private double _lastPosition;

    public async Task LoadAsync(string videoId)
    {
        VideoId = videoId;
        _errorReported = false;
        try
        {
#if WINDOWS
            await MapHostAsync();
            Source = new UrlWebViewSource { Url = $"https://{Host}/player.html?v={Uri.EscapeDataString(videoId)}" };
#else
            // WKWebView honours the base URL, so the page really is on our origin.
            var html = await ReadPlayerAsync();
            Source = new HtmlWebViewSource { Html = html.Replace("location.search", $"'?v={videoId}'"), BaseUrl = $"https://{Host}/" };
#endif
        }
        catch (Exception ex) { FileLog.Error("youtube load", ex); }
    }

    private static async Task<string> ReadPlayerAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("player.html");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

#if WINDOWS
    /// <summary>Copy the player page next to the app and serve it as
    /// https://strunika.local/ so the embed has an origin YouTube accepts.</summary>
    private async Task MapHostAsync()
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "web");
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "player.html");
        using (var src = await FileSystem.OpenAppPackageFileAsync("player.html"))
        using (var dst = File.Create(page))                      // always refresh: it ships with the app
            await src.CopyToAsync(dst);

        // The handler exists as soon as the view is in the tree; give it a moment
        // on a cold page.
        for (int i = 0; i < 20 && Handler?.PlatformView == null; i++) await Task.Delay(25);
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webview) return;
        await webview.EnsureCoreWebView2Async();
        webview.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
    }
#endif

    public Task PlayAsync() => RunAsync("strPlay()");
    public Task PauseAsync() => RunAsync("strPause()");
    public Task SeekAsync(double seconds) => RunAsync($"strSeek({seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    public Task SetRateAsync(double rate) => RunAsync($"strRate({rate.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    public Task SetVolumeAsync(double volume) => RunAsync($"strVolume({(int)Math.Clamp(volume * 100, 0, 100)})");

    /// <param name="PlayerState">YouTube's own code: -1 unstarted, 0 ended,
    /// 1 playing, 2 paused, 3 buffering, 5 cued.</param>
    /// <param name="WantPlay">The page's own record of whether play was asked for
    /// (by us or through YouTube's controls) and not yet paused/ended.</param>
    public readonly record struct State(double Position, double Duration, bool Playing, double Rate, int PlayerState, bool WantPlay);

    /// <summary>Null until the player page is ready.</summary>
    public async Task<State?> ProbeAsync()
    {
        try
        {
            var json = Unwrap(await EvaluateJavaScriptAsync("strState()"));
            if (_probesLogged < 5) { _probesLogged++; FileLog.Info($"youtube probe {_probesLogged}: {json ?? "<null>"}"); }
            if (string.IsNullOrEmpty(json) || json == "null") return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("e", out var e) && e.GetInt32() != 0 && !_errorReported)
            {
                _errorReported = true;
                PlayerError?.Invoke(this, e.GetInt32());
            }
            if (!r.GetProperty("ready").GetBoolean()) return null;
            var state = new State(r.GetProperty("t").GetDouble(), r.GetProperty("d").GetDouble(), r.GetProperty("p").GetBoolean(),
                                  r.GetProperty("r").GetDouble(), r.TryGetProperty("s", out var st) ? st.GetInt32() : 1,
                                  r.TryGetProperty("w", out var w) && w.GetBoolean());
            // Transitions only: a few lines per open, enough to replay a false start.
            if (state.PlayerState != _lastState || state.WantPlay != _lastWant || (!state.Playing && Math.Abs(state.Position - _lastPosition) > 0.3))
                FileLog.Info($"youtube state: s={state.PlayerState} t={state.Position:0.00} p={state.Playing} w={state.WantPlay}");
            _lastState = state.PlayerState; _lastWant = state.WantPlay; _lastPosition = state.Position;
            return state;
        }
        catch (Exception ex)
        {
            FileLog.Error("youtube probe", ex);
            return null;
        }
    }

    /// <summary>
    /// A JS string comes back differently per platform: WKWebView hands it over
    /// verbatim, WebView2 JSON-encodes it and MAUI then strips only the outer
    /// quotes — so <c>{"t":0}</c> arrives as <c>{\"t\":0}</c>. Undo whichever
    /// wrapping we got.
    /// </summary>
    private static string? Unwrap(string? raw)
    {
        if (raw == null) return null;
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return JsonSerializer.Deserialize<string>(raw);
        if (raw.Contains("\\\""))
            return JsonSerializer.Deserialize<string>("\"" + raw + "\"");
        return raw;
    }

    private async Task RunAsync(string script)
    {
        try { await EvaluateJavaScriptAsync(script); }
        catch (Exception ex) { FileLog.Error("youtube script", ex); }
    }
}

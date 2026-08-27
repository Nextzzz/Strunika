using System.Text.Json;
using Strunika.Core.Diagnostics;

namespace Strunika.Mobile.Services;

/// <summary>
/// Kill switches fetched at launch (never awaited by the UI). The one that
/// matters: <see cref="YouTubeAnalysis"/> — on-device extraction of YouTube
/// audio sits in a grey zone (App Store guideline 5.2.3, YouTube ToS), so it
/// must be possible to turn it off for every installed copy within the hour,
/// without a release. Values are cached in preferences; a failed fetch keeps
/// the last known ones, and the very first run defaults to "on".
/// </summary>
public static class RemoteFlags
{
    /// <summary>A tiny JSON: <c>{"youtube_analysis": true}</c>. Host it anywhere
    /// static (the repo must be public for the raw GitHub URL to work).</summary>
    public const string Url = "https://raw.githubusercontent.com/Nextzzz/Strunika/main/flags.json";

    public static bool YouTubeAnalysis => Preferences.Default.Get("flag_youtube_analysis", true);

    public static async Task RefreshAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var json = await http.GetStringAsync(Url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("youtube_analysis", out var yt) && (yt.ValueKind is JsonValueKind.True or JsonValueKind.False))
                Preferences.Default.Set("flag_youtube_analysis", yt.GetBoolean());
            FileLog.Info($"remote flags: youtube_analysis={YouTubeAnalysis}");
        }
        catch (Exception ex)
        {
            // Offline or not hosted yet: nothing changes.
            FileLog.Info($"remote flags: not refreshed ({ex.GetType().Name})");
        }
    }
}

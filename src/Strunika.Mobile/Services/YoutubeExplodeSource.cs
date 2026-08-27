using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Strunika.Mobile.Services;

/// <summary>
/// <see cref="IYouTubeSource"/> on YoutubeExplode — the same library on iOS
/// and on the Windows head, deliberately without yt-dlp (no external
/// binaries on a phone). Highest-bitrate MP4/AAC audio-only stream.
/// </summary>
public sealed class YoutubeExplodeSource : IYouTubeSource
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly YoutubeClient _client = new(Http);

    public string? TryParseVideoId(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        // Bare ids are accepted only when they look like one (11 url-safe chars);
        // everything else must be a real YouTube URL.
        bool looksLikeUrl = text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeUrl && text.Length != 11) return null;
        var id = VideoId.TryParse(text);
        return id?.Value;
    }

    public async Task<YouTubeInfo> GetInfoAsync(string videoId, CancellationToken ct = default)
    {
        var video = await _client.Videos.GetAsync(videoId, ct);
        var thumb = video.Thumbnails
            .Where(t => t.Resolution.Width <= 640)      // hq/mq: small enough for a card, always present
            .OrderByDescending(t => t.Resolution.Area)
            .FirstOrDefault() ?? video.Thumbnails.OrderBy(t => t.Resolution.Area).FirstOrDefault();
        return new YouTubeInfo(video.Id.Value, video.Title, video.Author.ChannelTitle, video.Duration, thumb?.Url);
    }

    public async Task<string> DownloadAudioAsync(string videoId, string targetDirectory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var manifest = await _client.Videos.Streams.GetManifestAsync(videoId, ct);
        // MP4/AAC only: neither AVFoundation (iOS) nor NAudio (Windows head)
        // decodes WebM/Opus, so a WebM download would only fail later.
        var stream = manifest.GetAudioOnlyStreams()
            .Where(s => s.Container == Container.Mp4)
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No MP4 audio stream.");
        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"yt-{videoId}.{stream.Container.Name}");
        await _client.Videos.Streams.DownloadAsync(stream, path, progress, ct);
        return path;
    }

    public async Task<bool> SaveThumbnailAsync(YouTubeInfo info, string targetPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.ThumbnailUrl)) return false;
        try
        {
            var bytes = await Http.GetByteArrayAsync(info.ThumbnailUrl, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllBytesAsync(targetPath, bytes, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Strunika.Core.Diagnostics.FileLog.Error("thumbnail", ex);
            return false;
        }
    }
}

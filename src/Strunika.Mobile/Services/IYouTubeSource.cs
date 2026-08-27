namespace Strunika.Mobile.Services;

public sealed record YouTubeInfo(string VideoId, string Title, string Author, TimeSpan? Duration, string? ThumbnailUrl);

/// <summary>
/// On-device YouTube access "like ChordAI": metadata for the library card
/// and a temporary audio stream for analysis. The audio is never kept and
/// never exported; playback (M3) goes through the official embed. Every
/// call can fail when YouTube changes — callers show the graceful
/// "YouTube unavailable" state, never a crash.
/// </summary>
public interface IYouTubeSource
{
    /// <summary>Video id from a URL or bare id; null when the text is not a YouTube link.</summary>
    string? TryParseVideoId(string text);

    Task<YouTubeInfo> GetInfoAsync(string videoId, CancellationToken ct = default);

    /// <summary>Downloads the best audio-only stream into <paramref name="targetDirectory"/>
    /// and returns the file path (m4a). Progress is 0–1.</summary>
    Task<string> DownloadAudioAsync(string videoId, string targetDirectory, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Saves the thumbnail to <paramref name="targetPath"/>; false when unavailable.</summary>
    Task<bool> SaveThumbnailAsync(YouTubeInfo info, string targetPath, CancellationToken ct = default);
}

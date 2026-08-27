namespace Strunika.Mobile.Services;

/// <summary>
/// One-shot UI sounds (the welcome strum). Assets live under
/// <c>Resources/Raw/sounds</c>; implementations copy them to the cache on
/// first use and play them without blocking. Never throws — a missing
/// sound is not worth an error in front of the user.
/// </summary>
public interface ISoundPlayer
{
    /// <param name="asset">Logical asset name, e.g. "sounds/greeting.wav".</param>
    Task PlayAsync(string asset);
}

public static class SoundAssets
{
    public const string Greeting = "sounds/greeting.wav";

    /// <summary>Unpacks a raw asset to the cache directory (idempotent) and returns its path.</summary>
    public static async Task<string?> EnsureAsync(string asset)
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, asset.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var source = await FileSystem.OpenAppPackageFileAsync(asset);
                await using var target = File.Create(path);
                await source.CopyToAsync(target);
            }
            return path;
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error($"sound asset {asset}", ex);
            return null;
        }
    }
}

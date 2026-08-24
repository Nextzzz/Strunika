using Strunika.Neural;

namespace Strunika.Mobile.Services;

/// <summary>
/// ONNX models travel inside the app package as MauiAssets; the ONNX
/// runtime needs real file paths, so on first use each model (with its
/// .json config) is copied into the cache directory. Idempotent by
/// file size check.
/// </summary>
public static class ModelStore
{
    public static async Task<string?> EnsureAsync(string name)
    {
        string dir = Path.Combine(FileSystem.CacheDirectory, "models");
        Directory.CreateDirectory(dir);
        foreach (var extension in new[] { ".onnx", ".json" })
        {
            string logical = $"models/{name}{extension}";
            string target = Path.Combine(dir, name + extension);
            try
            {
                using var source = await FileSystem.OpenAppPackageFileAsync(logical);
                if (File.Exists(target) && new FileInfo(target).Length > 0
                    && source.CanSeek && new FileInfo(target).Length == source.Length)
                    continue;
                using var output = File.Create(target);
                await source.CopyToAsync(output);
            }
            catch (FileNotFoundException)
            {
                return null; // model not bundled in this build
            }
        }
        return Path.Combine(dir, name + ".onnx");
    }

    /// <summary>Sliding detector for a bundled model, or null.</summary>
    public static async Task<SlidingNeuralChordDetector?> CreateDetectorAsync(string name)
    {
        string? path = await EnsureAsync(name);
        return path == null ? null : new SlidingNeuralChordDetector(path);
    }
}

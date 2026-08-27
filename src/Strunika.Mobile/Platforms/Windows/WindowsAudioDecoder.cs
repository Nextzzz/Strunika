using System.Diagnostics;
using Strunika.Core.Audio;
using Strunika.Core.Diagnostics;
using Strunika.Media;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>
/// Desktop decoder: NAudio (wav natively, mp3/m4a/aac via MediaFoundation).
/// MediaFoundation opens some YouTube DASH m4a files but yields no samples
/// at all; when that happens and the desktop tool-chain's ffmpeg is around
/// (<c>%LocalAppData%\Strunika\tools\ffmpeg.exe</c>, fetched by the WPF app)
/// it decodes through ffmpeg instead. iOS has no such problem — AVFoundation
/// plays fragmented MP4 natively — so this stays a dev-head convenience.
/// </summary>
public sealed class WindowsAudioDecoder : IAudioDecoder
{
    public Task<float[]> DecodeMonoAsync(string path, int sampleRate, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            float[]? samples = null;
            try
            {
                samples = AudioLoader.LoadMono(path, sampleRate).Samples;
            }
            catch (Exception ex) when (FindFfmpeg() != null)
            {
                FileLog.Error("NAudio decode failed, trying ffmpeg", ex);
            }
            if (samples != null && samples.Length >= sampleRate)
                return samples;

            var ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
                return samples ?? Array.Empty<float>();
            FileLog.Info($"NAudio returned {samples?.Length ?? 0} samples for {Path.GetFileName(path)}; decoding with ffmpeg");
            return DecodeWithFfmpeg(ffmpeg, path, sampleRate, ct);
        }, ct);

    private static string? FindFfmpeg()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strunika", "tools", "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static float[] DecodeWithFfmpeg(string ffmpeg, string path, int sampleRate, CancellationToken ct)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"strunika-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", path, "-vn", "-ac", "1", "-ar", sampleRate.ToString(), "-f", "wav", wav })
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi) ?? throw new IOException("ffmpeg did not start");
            using (ct.Register(() => { try { process.Kill(); } catch { /* exiting */ } }))
            {
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                ct.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                    throw new IOException($"ffmpeg failed: {stderr.Trim()}");
            }
            return WavFile.ReadMono(wav).Samples;
        }
        finally
        {
            try { File.Delete(wav); } catch { /* temp */ }
        }
    }
}

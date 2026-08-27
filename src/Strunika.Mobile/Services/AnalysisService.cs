using System.Collections.Concurrent;
using Strunika.Core.Analysis;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Data;
using Strunika.Mobile.Models;
using Strunika.Neural;

namespace Strunika.Mobile.Services;

/// <summary>Where a job is, for the library card.</summary>
public enum AnalysisStage { Queued, Downloading, Decoding, Recognizing, Beats, Saving }

/// <summary>
/// Background analysis queue: one song at a time (ONNX inference is the
/// bottleneck and shares the CPU with the UI), with progress, cancel and
/// failure per job. Pipeline = the desktop reference
/// (<c>Strunika.App SongViewModel.Analyze</c>): decode 44.1 k →
/// <see cref="HalfbandDecimator"/> → <see cref="NeuralChordRecognizer"/>
/// (self model, overlapping windows) → onset/tempo/beats →
/// <see cref="ChordTimeline.SnapToBeats"/> → SQLite. The recognizer is
/// cached per model; YouTube audio is a temp file removed afterwards.
/// </summary>
public sealed class AnalysisService
{
    private readonly ISongRepository _songs;
    private readonly IAudioDecoder _decoder;
    private readonly IYouTubeSource _youtube;
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancels = new();
    private readonly Dictionary<string, NeuralChordRecognizer> _recognizers = new();
    private Task _worker = Task.CompletedTask;
    private readonly object _lock = new();

    public AnalysisService(ISongRepository songs, IAudioDecoder decoder, IYouTubeSource youtube)
    {
        _songs = songs;
        _decoder = decoder;
        _youtube = youtube;
    }

    /// <summary>Raised on the main thread while a job runs.</summary>
    public event Action<int, AnalysisStage, double>? Progress;

    /// <summary>Raised on the main thread when a job ends: the song is
    /// Ready or Failed (already saved), or null when it was cancelled and removed.</summary>
    public event Action<int, Song?>? Finished;

    public bool IsQueued(int songId) => _cancels.ContainsKey(songId);

    /// <summary>Queues an existing library song (Pending/Failed/Ready) for analysis.</summary>
    public void Enqueue(int songId)
    {
        if (!_cancels.TryAdd(songId, new CancellationTokenSource())) return;
        _queue.Enqueue(songId);
        lock (_lock)
        {
            if (_worker.IsCompleted)
                _worker = Task.Run(DrainAsync);
        }
    }

    /// <summary>Stops the job; a song that never had a result is removed from the library.</summary>
    public void Cancel(int songId)
    {
        if (_cancels.TryGetValue(songId, out var cts))
            cts.Cancel();
    }

    private async Task DrainAsync()
    {
        while (_queue.TryDequeue(out int id))
        {
            if (!_cancels.TryGetValue(id, out var cts)) continue;
            try { await RunAsync(id, cts.Token); }
            catch (Exception ex) { FileLog.Error($"analysis {id}", ex); }
            finally
            {
                _cancels.TryRemove(id, out _);
                cts.Dispose();
            }
        }
    }

    private async Task RunAsync(int id, CancellationToken ct)
    {
        var song = await _songs.GetAsync(id);
        if (song == null) return;
        bool hadResult = song.Status == SongStatus.Ready;
        var previous = (song.Status, song.Error);
        song.Status = SongStatus.Analyzing;
        song.Error = null;
        await _songs.UpdateAsync(song);
        Report(id, AnalysisStage.Queued, 0);

        string? tempAudio = null;
        try
        {
            // Stage shares measured on the dev PC for a 3.5-minute song
            // (decode 1.0 s, halfband 0.2 s, CQT 1.8 s + windows 0.9 s,
            // novelty 1.05 s, tempo/beats ~0); the download gets a fixed
            // slice when there is one. Every long stage reports inside.
            bool download = song.Source == SongSource.YouTube;
            double dlShare = download ? 0.2 : 0, rest = 1 - dlShare;
            double decodeEnd = dlShare + rest * 0.2, recEnd = decodeEnd + rest * 0.55, beatEnd = recEnd + rest * 0.22;

            // 1. Audio at 44.1 k.
            string path;
            if (download)
            {
                var dl = new Progress<double>(p => Report(id, AnalysisStage.Downloading, p * dlShare));
                tempAudio = await _youtube.DownloadAudioAsync(song.SourceRef, Path.Combine(FileSystem.CacheDirectory, "yt"), dl, ct);
                path = tempAudio;
            }
            else
            {
                path = Path.Combine(FileSystem.AppDataDirectory, song.SourceRef);
            }
            Report(id, AnalysisStage.Decoding, dlShare);
            var samples44 = await _decoder.DecodeMonoAsync(path, 44100, ct);
            double duration = samples44.Length / 44100.0;
            if (duration < 1.0)
                throw new InvalidOperationException("too short");

            // 2. Chords.
            Report(id, AnalysisStage.Recognizing, decodeEnd);
            var samples22 = HalfbandDecimator.Decimate(samples44);
            var model = AppSettings.SongModel;
            var recognizer = await GetRecognizerAsync(model)
                ?? throw new InvalidOperationException("model missing");
            var rec = new Progress<double>(p => Report(id, AnalysisStage.Recognizing, decodeEnd + p * (recEnd - decodeEnd)));
            IReadOnlyList<(double Start, double End, string Label)> segments;
            string? key;
            lock (recognizer)   // one inference at a time per session
            {
                segments = recognizer.Recognize(samples22, 0.3, rec, ct)
                    .Select(s => (s.Start, s.End, ChordLabels.Pretty(s.Label)))
                    .ToList();
                key = recognizer.DetectedKey;
            }

            // 3. Beats.
            Report(id, AnalysisStage.Beats, recEnd);
            var onsets = new OnsetDetector();
            var beatProgress = new Progress<double>(p => Report(id, AnalysisStage.Beats, recEnd + p * (beatEnd - recEnd)));
            var novelty = onsets.NoveltyCurve(samples44, 44100, beatProgress, ct);
            double frameRate = onsets.FrameRate(44100);
            double bpm = new TempoEstimator().Estimate(novelty, frameRate);
            var beats = new BeatTracker().Track(novelty, frameRate, bpm)
                .Select(f => Math.Round((f * onsets.Hop + onsets.NFft / 2.0) / 44100.0, 3))
                .ToList();
            if (AppSettings.BeatSnap)
                segments = ChordTimeline.SnapToBeats(segments, beats);

            // 4. Save.
            Report(id, AnalysisStage.Saving, beatEnd);
            ct.ThrowIfCancellationRequested();
            song.DurationSec = duration;
            song.Bpm = Math.Round(bpm);
            song.Key = key;
            song.Model = model;
            song.Segments = segments.Select(s => new ChordSegmentDto(s.Start, s.End, s.Label)).ToList();
            song.Beats = beats.ToArray();
            song.Peaks = Strunika.Core.Audio.Waveform.Peaks(samples44, 44100);
            song.PeaksVersion = Strunika.Core.Audio.Waveform.Version;
            song.Edited = false;
            song.Status = SongStatus.Ready;
            await _songs.UpdateAsync(song);
            Finish(id, song);
        }
        catch (OperationCanceledException)
        {
            if (hadResult)
            {
                // Re-analysis aborted: the old result stands.
                song.Status = SongStatus.Ready;
                song.Error = null;
                await _songs.UpdateAsync(song);
                Finish(id, song);
            }
            else
            {
                await RemoveAsync(song);
                Finish(id, null);
            }
        }
        catch (Exception ex)
        {
            FileLog.Error($"analysis of song {id} failed", ex);
            song.Status = hadResult ? SongStatus.Ready : SongStatus.Failed;
            song.Error = hadResult ? previous.Error : Describe(ex);
            await _songs.UpdateAsync(song);
            Finish(id, song);
        }
        finally
        {
            if (tempAudio != null)
                try { File.Delete(tempAudio); } catch { /* cache */ }
        }
    }

    /// <summary>Deletes a song with its private files (audio copy, take, thumbnail).</summary>
    public async Task RemoveAsync(Song song)
    {
        Cancel(song.Id);
        await _songs.DeleteAsync(song.Id);
        if (song.Source != SongSource.YouTube && !string.IsNullOrEmpty(song.SourceRef))
            TryDelete(Path.Combine(FileSystem.AppDataDirectory, song.SourceRef));
        if (!string.IsNullOrEmpty(song.ThumbnailPath))
            TryDelete(Path.Combine(FileSystem.AppDataDirectory, song.ThumbnailPath));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static string Describe(Exception ex) => ex switch
    {
        InvalidOperationException { Message: "too short" } => "TooShort",
        InvalidOperationException { Message: "model missing" } => "ModelMissing",
        HttpRequestException or YoutubeExplode.Exceptions.YoutubeExplodeException => "YouTube",
        IOException or UnauthorizedAccessException => "File",
        _ => "Unknown",
    };

    private async Task<NeuralChordRecognizer?> GetRecognizerAsync(string model)
    {
        lock (_recognizers)
        {
            if (_recognizers.TryGetValue(model, out var cached)) return cached;
        }
        var path = await ModelStore.EnsureAsync(model);
        if (path == null) return null;
        var recognizer = new NeuralChordRecognizer(path) { OverlapWindows = true };
        lock (_recognizers)
        {
            if (_recognizers.TryGetValue(model, out var raced)) { recognizer.Dispose(); return raced; }
            _recognizers[model] = recognizer;
            return recognizer;
        }
    }

    private void Report(int id, AnalysisStage stage, double progress) =>
        MainThread.BeginInvokeOnMainThread(() => Progress?.Invoke(id, stage, Math.Clamp(progress, 0, 1)));

    private void Finish(int id, Song? song) =>
        MainThread.BeginInvokeOnMainThread(() => Finished?.Invoke(id, song));
}

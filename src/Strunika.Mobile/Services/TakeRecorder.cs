using Strunika.Core.Audio;

namespace Strunika.Mobile.Services;

/// <summary>
/// Records the microphone into a WAV in the app's data directory (the
/// "Запис" way of adding a song). Keeps everything in memory — a take is
/// minutes, not hours — and exposes level + elapsed time for the sheet.
/// </summary>
public sealed class TakeRecorder
{
    private readonly IMicrophoneSource _microphone;
    private readonly List<float[]> _chunks = new();
    private long _samples;
    private DateTime _startedAt;

    public TakeRecorder(IMicrophoneSource microphone) => _microphone = microphone;

    public bool IsRecording { get; private set; }

    /// <summary>Peak level of the last chunk, 0–1.</summary>
    public event Action<float>? Level;

    public TimeSpan Elapsed => IsRecording ? DateTime.Now - _startedAt : TimeSpan.Zero;

    public static string Directory => Path.Combine(FileSystem.AppDataDirectory, "recordings");

    public async Task<bool> StartAsync()
    {
        if (IsRecording) return true;
        _chunks.Clear();
        _samples = 0;
        _microphone.ChunkAvailable += OnChunk;
        if (!await _microphone.StartAsync())
        {
            _microphone.ChunkAvailable -= OnChunk;
            return false;
        }
        _startedAt = DateTime.Now;
        IsRecording = true;
        return true;
    }

    /// <summary>Stops and writes the take; returns the file path and its
    /// duration, or null when nothing was captured.</summary>
    public (string Path, double Seconds)? Stop()
    {
        if (!IsRecording) return null;
        IsRecording = false;
        _microphone.ChunkAvailable -= OnChunk;
        _microphone.Stop();
        if (_samples == 0) return null;

        var all = new float[_samples];
        int at = 0;
        foreach (var c in _chunks) { c.CopyTo(all, at); at += c.Length; }
        _chunks.Clear();

        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"take-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        WavFile.Write(path, all, IMicrophoneSource.SampleRate);
        return (path, all.Length / (double)IMicrophoneSource.SampleRate);
    }

    public void Cancel()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _microphone.ChunkAvailable -= OnChunk;
        _microphone.Stop();
        _chunks.Clear();
        _samples = 0;
    }

    private void OnChunk(float[] chunk)
    {
        var copy = (float[])chunk.Clone();
        _chunks.Add(copy);
        _samples += copy.Length;
        float peak = 0;
        foreach (var v in copy) peak = Math.Max(peak, Math.Abs(v));
        Level?.Invoke(peak);
    }
}

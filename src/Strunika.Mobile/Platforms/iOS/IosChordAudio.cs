using AVFoundation;
using Foundation;
using Strunika.Core.Audio;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// Chord playback through AVAudioPlayer over an in-memory WAV, like the
/// metronome ticks. Shapes are rendered once and remembered — the same
/// position is usually heard twice. Unverified until a device build.
/// </summary>
public sealed class IosChordAudio : IChordAudio
{
    private readonly Dictionary<string, AVAudioPlayer> _cache = new();
    private AVAudioPlayer? _playing;

    public double Volume { get; set; } = 1.0;

    public event EventHandler? Finished;

    public void Strum(IReadOnlyList<int> frets)
    {
        var key = string.Join(",", frets);
        if (!_cache.TryGetValue(key, out var player))
        {
            var samples = PluckSynth.Strum(frets);
            if (samples.Length == 0) return;
            using var ms = new MemoryStream();
            WavFile.Write(ms, samples, PluckSynth.SampleRate);
            player = AVAudioPlayer.FromData(NSData.FromArray(ms.ToArray()), out _)!;
            player.PrepareToPlay();
            _cache[key] = player;
        }
        // A new strum replaces the ringing one.
        if (_playing != null && _playing != player) _playing.Stop();
        _playing = player;
        player.Volume = (float)Math.Clamp(Volume, 0, 1);
        player.CurrentTime = 0;
        player.FinishedPlaying -= OnFinished;
        player.FinishedPlaying += OnFinished;
        player.Play();
    }

    private void OnFinished(object? sender, AVStatusEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => Finished?.Invoke(this, EventArgs.Empty));

    public void Stop()
    {
        _playing?.Stop();
        _playing = null;
        MainThread.BeginInvokeOnMainThread(() => Finished?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        foreach (var player in _cache.Values) player.Dispose();
        _cache.Clear();
    }
}

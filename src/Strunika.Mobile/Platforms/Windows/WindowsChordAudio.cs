using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Strunika.Core.Audio;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>
/// Chord playback through one always-open output and a mixer, the same shape
/// as the metronome: a strum costs a buffer, not a device open. Shapes are
/// rendered once and remembered — the same position is usually heard twice.
/// </summary>
public sealed class WindowsChordAudio : IChordAudio
{
    private readonly WaveOutEvent _output = new() { DesiredLatency = 80 };
    private readonly MixingSampleProvider _mixer;
    private readonly Dictionary<string, float[]> _cache = new();
    private CancellationTokenSource? _ringing;

    public double Volume { get; set; } = 1.0;

    public event EventHandler? Finished;

    public WindowsChordAudio()
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(PluckSynth.SampleRate, 1)) { ReadFully = true };
        _output.Init(_mixer);
        _output.Play();
    }

    public void Strum(IReadOnlyList<int> frets)
    {
        var key = string.Join(",", frets);
        if (!_cache.TryGetValue(key, out var samples))
            _cache[key] = samples = PluckSynth.Strum(frets);
        if (samples.Length == 0) return;

        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var source = new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(PluckSynth.SampleRate, 1)).ToSampleProvider();
        _mixer.RemoveAllMixerInputs();                               // a new strum replaces the ringing one
        _mixer.AddMixerInput(new VolumeSampleProvider(source) { Volume = (float)Math.Clamp(Volume, 0, 1) });

        // The mixer has no "input ended" of its own worth wiring: the length of
        // the buffer is known exactly, so the end is simply timed.
        _ringing?.Cancel();
        _ringing = new CancellationTokenSource();
        var token = _ringing.Token;
        double seconds = samples.Length / (double)PluckSynth.SampleRate;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => Finished?.Invoke(this, EventArgs.Empty));
        }, TaskScheduler.Default);
    }

    public void Stop()
    {
        _ringing?.Cancel();
        _mixer.RemoveAllMixerInputs();
        MainThread.BeginInvokeOnMainThread(() => Finished?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        _ringing?.Cancel();
        _output.Stop();
        _output.Dispose();
    }
}

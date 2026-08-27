using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>NAudio file playback (wav/mp3/m4a via MediaFoundation). No rate change on the dev head.</summary>
public sealed class WindowsAudioPlayer : IAudioPlayer
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public Task LoadAsync(string path)
    {
        Dispose();
        _reader = new AudioFileReader(path);
        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.Init(_reader);
        return Task.CompletedTask;
    }

    public void Play() => _output?.Play();

    public void Pause() => _output?.Pause();

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

    public double Position
    {
        get => _reader?.CurrentTime.TotalSeconds ?? 0;
        set { if (_reader != null) _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value, 0, Math.Max(0, Duration))); }
    }

    public double Rate { get; set; } = 1.0;   // not supported by NAudio without a time-stretch library

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_reader != null) _reader.Volume = (float)_volume;
        }
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }
}

/// <summary>Metronome clicks through one always-open output and a mixer, so a
/// click costs nothing but a tiny buffer.</summary>
public sealed class WindowsClickPlayer : IClickPlayer
{
    public double Volume { get; set; } = 1.0;

    private readonly WaveOutEvent _output = new() { DesiredLatency = 60 };
    private readonly MixingSampleProvider _mixer;
    private readonly float[] _tick, _accent;

    public WindowsClickPlayer()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };
        _tick = MetronomeClick.Render(1100, 0.5f);
        _accent = MetronomeClick.Render(1650, 0.7f);
        _output.Init(_mixer);
        _output.Play();
    }

    public void Click(bool accent)
    {
        var data = accent ? _accent : _tick;
        var source = new RawSourceWaveStream(new MemoryStream(FloatBytes(data)), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)).ToSampleProvider();
        _mixer.AddMixerInput(new VolumeSampleProvider(source) { Volume = (float)Math.Clamp(Volume, 0, 1) });
    }

    private static byte[] FloatBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        _output.Stop();
        _output.Dispose();
    }
}

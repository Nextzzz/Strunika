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

    private WaveOutEvent? _output;
    private readonly MixingSampleProvider _mixer;
    private readonly float[] _tick, _accent;
    private readonly object _gate = new();
    private int _restarts;

    public WindowsClickPlayer()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };
        _tick = MetronomeClick.Render(1100, 0.95f);
        _accent = MetronomeClick.Render(1650, 1.0f);
        Open();
    }

    /// <summary>A fresh output over the mixer. WaveOut stops for good when the
    /// device goes away or the default device changes (headphones, a monitor
    /// with speakers), and a stopped output is silent forever: the metronome
    /// went quiet mid-session and never came back. Every tick checks that
    /// the output is playing and reopens it when it is not; a stop is logged
    /// with its reason.</summary>
    private void Open()
    {
        lock (_gate)
        {
            try { _output?.Dispose(); } catch { /* already gone */ }
            _output = new WaveOutEvent { DesiredLatency = 60 };
            _output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null && _restarts < 5)
                    Strunika.Core.Diagnostics.FileLog.Error("click output stopped", e.Exception);
            };
            _output.Init(_mixer);
            _output.Play();
        }
    }

    private void Ensure()
    {
        if (_output?.PlaybackState == PlaybackState.Playing) return;
        if (_restarts++ < 5) Strunika.Core.Diagnostics.FileLog.Info($"click output reopened ({_output?.PlaybackState})");
        Open();
    }

    public void Click(bool accent)
    {
        Ensure();
        var data = accent ? _accent : _tick;
        var source = new RawSourceWaveStream(new MemoryStream(FloatBytes(data)), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)).ToSampleProvider();
        _mixer.AddMixerInput(new VolumeSampleProvider(source) { Volume = (float)Math.Clamp(Volume, 0, 1) });
    }

    /// <summary>What the output pipeline adds between a sample entering the
    /// mixer and it being heard: the two 30 ms buffers of a 60 ms WaveOut
    /// (30–60 ms, 45 on average) and the device's own few milliseconds. Taken
    /// off every tick's delay; a timer on top of this was a beat late.</summary>
    private const double OutputLatency = 0.05;

    /// <summary>The tick is placed in the mixer now, behind exactly as much
    /// silence as is left of the delay once the pipeline's latency is taken
    /// off: sample-accurate, no timer, no thread.</summary>
    public void ClickAt(double delaySeconds, bool accent)
    {
        double lead = delaySeconds - OutputLatency;
        if (lead <= 0.001) { Click(accent); return; }
        Ensure();
        var data = accent ? _accent : _tick;
        var source = new RawSourceWaveStream(new MemoryStream(FloatBytes(data)), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)).ToSampleProvider();
        var delayed = new OffsetSampleProvider(source) { DelayBy = TimeSpan.FromSeconds(lead) };
        _mixer.AddMixerInput(new VolumeSampleProvider(delayed) { Volume = (float)Math.Clamp(Volume, 0, 1) });
    }

    public void Cancel() => _mixer.RemoveAllMixerInputs();

    private static byte[] FloatBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _output?.Stop(); _output?.Dispose(); } catch { /* already gone */ }
            _output = null;
        }
    }
}

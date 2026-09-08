using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>NAudio file playback (wav/mp3/m4a via MediaFoundation). No rate
/// change on the dev head. The metronome's ticks are written into the song's
/// own samples on the way to the output (<see cref="TickingSource"/>), so
/// they sit on the beat whatever the output buffers; and the position is the
/// sample being heard, not the one just read — WaveOut holds 60–120 ms of
/// audio the reader is already past, which had the squares (and, before, the
/// ticks) that much early.</summary>
public sealed class WindowsAudioPlayer : IAudioPlayer, ITickTrack
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private TickingSource? _source;
    private double[] _beats = Array.Empty<double>();
    private bool _ticksOn;
    private double _tickVolume = 1.0;

    public Task LoadAsync(string path)
    {
        Dispose();
        _reader = new AudioFileReader(path) { Volume = (float)_volume };
        _source = new TickingSource(_reader);
        _source.Ticks.Set(_beats, _ticksOn, _tickVolume);
        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.Init(_source);
        return Task.CompletedTask;
    }

    public void Play() => _output?.Play();

    public void Pause() => _output?.Pause();

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

    public double Position
    {
        get
        {
            if (_reader == null || _source == null || _output == null) return 0;
            // Frames handed to the output but not yet played sit between the
            // reader's head and the ear.
            long played = _output.GetPosition() / _reader.WaveFormat.BlockAlign;
            double buffered = Math.Max(0, _source.Delivered - played) / (double)_reader.WaveFormat.SampleRate;
            return Math.Max(0, _reader.CurrentTime.TotalSeconds - buffered);
        }
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

    public void SetTicks(double[] beats, bool enabled, double volume)
    {
        _beats = beats; _ticksOn = enabled; _tickVolume = volume;
        _source?.Ticks.Set(beats, enabled, volume);
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _source = null;
    }

    /// <summary>The song's samples with the ticks added, and a count of the
    /// frames delivered so far (the output's position is compared to it).</summary>
    private sealed class TickingSource : ISampleProvider
    {
        private readonly ISampleProvider _inner;
        private readonly int _channels;
        private long _frames;
        public TickMixer Ticks { get; }
        public WaveFormat WaveFormat => _inner.WaveFormat;
        public long Delivered => Interlocked.Read(ref _frames);

        public TickingSource(AudioFileReader reader)
        {
            _inner = reader;
            _channels = reader.WaveFormat.Channels;
            Ticks = new TickMixer(reader.WaveFormat.SampleRate);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // The frame the block starts at is the reader's own position: it
            // moves on a seek and the ticks move with it.
            var reader = (AudioFileReader)_inner;
            long first = (long)Math.Round(reader.CurrentTime.TotalSeconds * WaveFormat.SampleRate);
            int read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                Ticks.MixInterleaved(first, new Span<float>(buffer, offset, read), _channels);
                Interlocked.Add(ref _frames, read / _channels);
            }
            return read;
        }
    }
}

/// <summary>Metronome clicks for a YouTube song through one always-open
/// output: a tick is written at an absolute position in the output stream,
/// counted in frames the device has played plus the delay — not "added to a
/// mixer now" and read whenever the output thread next woke, which moved
/// every tick by up to a buffer (30 ms).</summary>
public sealed class WindowsClickPlayer : IClickPlayer
{
    public double Volume { get; set; } = 1.0;
    /// <summary>What the shared-mode engine adds after WaveOut reports a
    /// frame played: an estimate for the dev head, not a measurement.</summary>
    public double Latency => DeviceLatency;
    private const double DeviceLatency = 0.02;
    private const int SampleRate = 44100;

    private WaveOutEvent? _output;
    private ScheduledTicks? _stream;
    private readonly float[] _tick, _accent;
    private readonly object _gate = new();
    private int _restarts, _logs = 3;

    public WindowsClickPlayer()
    {
        _tick = MetronomeClick.Render(MetronomeClick.TickHz, 0.95f);
        _accent = MetronomeClick.Render(MetronomeClick.AccentHz, 1.0f);
        Open();
    }

    /// <summary>A fresh output over a fresh stream. WaveOut stops for good when
    /// the device goes away or the default device changes (headphones, a
    /// monitor with speakers), and a stopped output is silent forever: every
    /// tick checks that the output is playing and reopens it when it is not.</summary>
    private void Open()
    {
        lock (_gate)
        {
            try { _output?.Dispose(); } catch { /* already gone */ }
            _stream = new ScheduledTicks(SampleRate);
            _output = new WaveOutEvent { DesiredLatency = 60 };
            _output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null && _restarts < 5) FileLog.Error("click output stopped", e.Exception);
            };
            _output.Init(_stream);
            _output.Play();
        }
    }

    private void Ensure()
    {
        if (_output?.PlaybackState == PlaybackState.Playing) return;
        if (_restarts++ < 5) FileLog.Info($"click output reopened ({_output?.PlaybackState})");
        Open();
    }

    public void Prepare() => Ensure();

    public void Click(bool accent) => ClickAt(0, accent);

    public void ClickAt(double delaySeconds, bool accent)
    {
        lock (_gate)
        {
            Ensure();
            if (_output == null || _stream == null) return;
            long played = _output.GetPosition() / 4;             // mono float: 4 bytes a frame
            long at = played + (long)Math.Round((delaySeconds - DeviceLatency) * SampleRate);
            long placedAt = _stream.Add(at, accent ? _accent : _tick, (float)Math.Clamp(Volume, 0, 1));
            if (_logs > 0)
            {
                _logs--;
                FileLog.Info($"click: at frame {at} ({(at - played) * 1000.0 / SampleRate:0} ms past the played frame), stream rendered to {_stream.Rendered}{(placedAt > at ? $", late by {(placedAt - at) * 1000.0 / SampleRate:0} ms" : "")}");
            }
        }
    }

    public void Cancel()
    {
        lock (_gate) _stream?.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _output?.Stop(); _output?.Dispose(); } catch { /* already gone */ }
            _output = null;
        }
    }

    /// <summary>Silence with ticks at absolute frame positions. The output
    /// thread reads it in buffers; every tick that overlaps a buffer is added
    /// where it falls.</summary>
    private sealed class ScheduledTicks : ISampleProvider
    {
        private readonly List<(long Start, float[] Samples, float Volume)> _pending = new();
        private readonly object _gate = new();
        private long _rendered;
        public WaveFormat WaveFormat { get; }
        public long Rendered { get { lock (_gate) return _rendered; } }

        public ScheduledTicks(int sampleRate) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        /// <summary>Returns where the tick was actually placed: a start already
        /// rendered is moved to the next frame to be rendered.</summary>
        public long Add(long start, float[] samples, float volume)
        {
            lock (_gate)
            {
                if (start < _rendered) start = _rendered;
                _pending.Add((start, samples, volume));
                return start;
            }
        }

        public void Clear() { lock (_gate) _pending.Clear(); }

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            lock (_gate)
            {
                long first = _rendered, last = _rendered + count;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var (start, samples, volume) = _pending[i];
                    long end = start + samples.Length;
                    if (end <= first) { _pending.RemoveAt(i); continue; }
                    if (start >= last) continue;
                    for (long f = Math.Max(start, first); f < Math.Min(end, last); f++)
                        buffer[offset + (int)(f - first)] += samples[f - start] * volume;
                    if (end <= last) _pending.RemoveAt(i);
                }
                _rendered = last;
            }
            return count;
        }
    }
}

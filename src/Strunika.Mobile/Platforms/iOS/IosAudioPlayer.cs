using AVFoundation;
using Foundation;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// A file song on the shared AVAudioEngine: the file is read in small blocks,
/// each block gets the song's volume and the metronome's ticks written into
/// it at their beats' samples, and is scheduled on a player node behind an
/// AVAudioUnitTimePitch for the pitch-preserving speed. Because a tick and
/// the song are the same stream, no buffer, route latency (AirPods add
/// 163 ms) or speed can pull them apart — the metronome needs no clock and no
/// calibration for a file. AVAudioPlayer, used before, reported a position on
/// the hardware's timeline while the ticks were placed as "heard" time, and
/// the two disagreed by exactly the Bluetooth latency.
/// <para>Blocks are 2048 frames (~46 ms) with four in flight, so the metronome
/// switch, the volume and the speed take effect within ~200 ms.</para>
/// </summary>
public sealed class IosAudioPlayer : IAudioPlayer, ITickTrack
{
    private const uint ChunkFrames = 2048;
    private const int InFlight = 4;

    private readonly AVAudioPlayerNode _node = new();
    private readonly AVAudioUnitTimePitch _pitch = new();
    private AVAudioFile? _file;
    private AVAudioFormat? _format;
    private AVAudioPcmBuffer[] _ring = Array.Empty<AVAudioPcmBuffer>();
    private TickMixer? _ticks;
    private double[] _beats = Array.Empty<double>();
    private bool _ticksOn;
    private double _tickVolume = 1.0;

    private int _sampleRate = 44100, _channels = 2;
    private long _length;
    private long _cursor;              // next file frame to read
    private long _consumed;            // file frames the player has taken, as of the last block's completion
    private long _consumedAt;          // Stopwatch stamp of that completion
    private int _queued;               // blocks scheduled and not yet consumed
    private int _generation;           // bumped by every seek/stop: completions of the blocks before it are ignored
    private bool _playing, _ended, _attached;
    private double _rate = 1.0, _volume = 1.0, _heardLag;
    private double _pausedInto;        // real seconds into the current block when paused, so the position holds still
    private readonly Action _onEngineStopped;

    public IosAudioPlayer()
    {
        _onEngineStopped = OnEngineStopped;
        SharedAudioEngine.Stopped += _onEngineStopped;
    }

    public Task LoadAsync(string path)
    {
        lock (SharedAudioEngine.Gate)
        {
            Unload();
            AudioSessions.ForPlayback();
            _file = new AVAudioFile(NSUrl.FromFilename(path), out NSError? error);
            if (error != null)
            {
                _file = null;
                throw new IOException(error.LocalizedDescription);
            }
            _format = _file.ProcessingFormat;                    // float32, non-interleaved, the file's own rate
            _sampleRate = (int)_format.SampleRate;
            _channels = (int)_format.ChannelCount;
            _length = _file.Length;
            _ticks = new TickMixer(_sampleRate);
            _ticks.Set(_beats, _ticksOn, _tickVolume);
            _ring = new AVAudioPcmBuffer[InFlight];
            for (int i = 0; i < InFlight; i++) _ring[i] = new AVAudioPcmBuffer(_format, ChunkFrames);

            var engine = SharedAudioEngine.Engine;
            if (!_attached)
            {
                engine.AttachNode(_node);
                engine.AttachNode(_pitch);
                _attached = true;
            }
            engine.Connect(_node, _pitch, _format);
            engine.Connect(_pitch, engine.MainMixerNode, _format);
            _pitch.Rate = (float)_rate;

            _cursor = 0; _consumed = 0; _queued = 0; _ended = _length == 0; _playing = false;
            Prime();
        }
        return Task.CompletedTask;
    }

    private void Unload()
    {
        _generation++;
        if (_attached) _node.Stop();
        _queued = 0;
        foreach (var b in _ring) b.Dispose();
        _ring = Array.Empty<AVAudioPcmBuffer>();
        _file?.Dispose();
        _file = null;
    }

    /// <summary>Schedules blocks into every free slot, from the cursor on.</summary>
    private void Prime()
    {
        int generation = _generation;
        for (int slot = 0; slot < _ring.Length; slot++)
            if (!Schedule(slot, generation)) break;
    }

    /// <summary>Reads the next block into the slot's buffer, writes the volume
    /// and the ticks into it and schedules it after the blocks already queued.
    /// False when the file has no more frames. Under the gate.</summary>
    private bool Schedule(int slot, int generation)
    {
        if (_file == null || _cursor >= _length) return false;
        var buffer = _ring[slot];
        _file.FramePosition = _cursor;
        if (!_file.ReadIntoBuffer(buffer, ChunkFrames, out NSError? error) || error != null)
        {
            FileLog.Error("song read: " + error?.LocalizedDescription);
            return false;
        }
        int frames = (int)buffer.FrameLength;
        if (frames == 0) return false;
        long first = _cursor;
        _cursor += frames;

        float volume = (float)_volume;
        unsafe
        {
            float** data = (float**)buffer.FloatChannelData;
            for (int c = 0; c < _channels; c++)
            {
                var channel = new Span<float>(data[c], frames);
                if (volume != 1f) for (int i = 0; i < frames; i++) channel[i] *= volume;
                _ticks?.MixChannel(first, channel);
            }
        }
        _queued++;
        _node.ScheduleBuffer(buffer, null, 0, AVAudioPlayerNodeCompletionCallbackType.Consumed,
            _ => OnConsumed(generation, slot, frames));
        return true;
    }

    private void OnConsumed(int generation, int slot, int frames)
    {
        lock (SharedAudioEngine.Gate)
        {
            if (generation != _generation) return;               // a block from before a seek or stop
            _consumed += frames;
            _consumedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            _queued--;
            if (!Schedule(slot, generation) && _queued == 0)
            {
                _ended = true;                                   // the last block is in the output
                _playing = false;
            }
        }
    }

    public void Play()
    {
        lock (SharedAudioEngine.Gate)
        {
            if (_file == null) return;
            if (_ended) Reposition(0);
            if (!SharedAudioEngine.Ensure()) return;
            RefreshLag();
            if (_queued == 0) Prime();
            _node.Play();
            _playing = true;
            // The block clock resumes so that the position carries on from where the pause froze it.
            _consumedAt = System.Diagnostics.Stopwatch.GetTimestamp() - (long)((_pausedInto + _heardLag) * System.Diagnostics.Stopwatch.Frequency);
            _pausedInto = 0;
        }
    }

    public void Pause()
    {
        lock (SharedAudioEngine.Gate)
        {
            if (!_playing) return;
            // Frozen at the heard position (may be a little before the block's start).
            _pausedInto = _consumedAt == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(_consumedAt).TotalSeconds - _heardLag;
            _node.Pause();                                       // the queued blocks wait
            _playing = false;
        }
    }

    /// <summary>What lies between a frame leaving the player node and being
    /// heard: the time-pitch unit's own latency, the engine's IO buffer and the
    /// route's output latency. Taken off the position so the squares follow
    /// the sound, not the render.</summary>
    private void RefreshLag()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            _heardLag = _pitch.Latency + session.IOBufferDuration + session.OutputLatency;
        }
        catch { _heardLag = 0; }
    }

    public bool IsPlaying { get { lock (SharedAudioEngine.Gate) return _playing && !_ended; } }

    public double Duration => _length / (double)_sampleRate;

    public double Position
    {
        get
        {
            lock (SharedAudioEngine.Gate)
            {
                if (_file == null) return 0;
                if (_ended) return Duration;
                // Real seconds into the current block, less what has not reached
                // the ear yet; paused, the value frozen at the pause (a fresh
                // seek: exactly the target). Song seconds, so scaled by the speed.
                double into = _playing && _consumedAt != 0
                    ? System.Diagnostics.Stopwatch.GetElapsedTime(_consumedAt).TotalSeconds - _heardLag
                    : _pausedInto;
                double seconds = _consumed / (double)_sampleRate + into * _rate;
                return Math.Clamp(seconds, 0, Duration);
            }
        }
        set
        {
            lock (SharedAudioEngine.Gate)
            {
                if (_file == null) return;
                long frame = (long)Math.Round(Math.Clamp(value, 0, Duration) * _sampleRate);
                bool playing = _playing;
                Reposition(frame);
                if (playing && SharedAudioEngine.Ensure()) { _node.Play(); _playing = true; }
            }
        }
    }

    /// <summary>Drops every queued block and queues again from
    /// <paramref name="frame"/>. The node is left stopped. Under the gate.</summary>
    private void Reposition(long frame)
    {
        _generation++;                                           // before Stop: it may fire the old completions
        _node.Stop();
        _queued = 0;
        _cursor = frame;
        _consumed = frame;
        _consumedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _pausedInto = 0;
        _ended = frame >= _length;
        _playing = false;
        Prime();
    }

    public double Rate
    {
        get => _rate;
        set
        {
            _rate = Math.Clamp(value, 0.5, 1.25);
            lock (SharedAudioEngine.Gate) _pitch.Rate = (float)_rate;
        }
    }

    /// <summary>Written into the blocks as they are read, so the slider is heard
    /// within ~200 ms; the ticks keep their own level on top.</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 1);
    }

    public void SetTicks(double[] beats, bool enabled, double volume)
    {
        lock (SharedAudioEngine.Gate)
        {
            _beats = beats;
            _ticksOn = enabled;
            _tickVolume = volume;
            _ticks?.Set(beats, enabled, volume);
        }
    }

    /// <summary>Headphones in or out stopped the engine: pause where the sound
    /// was, ready to go on from there.</summary>
    private void OnEngineStopped()
    {
        lock (SharedAudioEngine.Gate)
        {
            if (_file == null) return;
            bool playing = _playing;
            long frame = (long)Math.Round(Position * _sampleRate);
            Reposition(frame);
            if (playing) FileLog.Info($"song player: paused at {frame / (double)_sampleRate:0.00} s by a route change");
        }
    }

    public void Dispose()
    {
        SharedAudioEngine.Stopped -= _onEngineStopped;
        lock (SharedAudioEngine.Gate)
        {
            Unload();
            if (_attached)
            {
                try
                {
                    SharedAudioEngine.Engine.DetachNode(_node);
                    SharedAudioEngine.Engine.DetachNode(_pitch);
                }
                catch (Exception ex) { FileLog.Error("song player detach", ex); }
                _attached = false;
            }
            _node.Dispose();
            _pitch.Dispose();
        }
    }
}

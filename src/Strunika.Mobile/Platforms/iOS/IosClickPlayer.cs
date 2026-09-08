using AudioToolbox;
using AVFoundation;
using CoreAnimation;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The metronome for a YouTube song: an AVAudioSourceNode on the shared
/// engine that renders its own stream of silence with the ticks written in at
/// absolute frames. Every render block brings the HAL's time stamp for its
/// first frame, so the stream has its own clock — a pair (frame, host time)
/// refreshed each block — and a tick asked for a host time is a frame number,
/// nothing more. (A file song does not come here at all — its ticks are mixed
/// into its own stream, see <see cref="ITickTrack"/>.)
/// <para>
/// History (2026-09-08/09): AVAudioPlayerNode with ScheduleBuffer was tried
/// three ways. Measuring "what is left of the delay" on a worker and taking
/// the session's buffer and output latency off it went negative on AirPods
/// (173 ms) and every tick played at once, 65 ms late; a 60 ms cap made them
/// 100 ms late; anchoring on the node's LastRenderTime never got a valid
/// host/sample pair on the device ("no render clock yet") and the ticks
/// played the moment they were asked for, a lookahead early. The render
/// callback's own stamp is the one clock that is always there.
/// </para>
/// </summary>
public sealed class IosClickPlayer : IClickPlayer
{
    private const int SampleRate = MetronomeClick.SampleRate;

    public double Volume { get; set; } = 1.0;
    public double Latency => Volatile.Read(ref _outputLatency) + Volatile.Read(ref _ioBuffer);

    private readonly struct Pending
    {
        public readonly long Start; public readonly float[] Samples; public readonly float Volume;
        public Pending(long start, float[] samples, float volume) { Start = start; Samples = samples; Volume = volume; }
    }

    private readonly AVAudioSourceNode _source;
    private readonly AVAudioSourceNodeRenderHandler3 _render;   // kept: the node holds a block over it
    private readonly float[] _tick, _accent;
    /// <summary>The tick list and the stream clock. Held for microseconds by
    /// the render thread and the caller alike.</summary>
    private readonly object _gate = new();
    private readonly List<Pending> _pending = new();
    private long _rendered;                                      // frames rendered so far: the stream's own clock
    private long _clockFrame;                                    // the frame the latest block began at ...
    private ulong _clockHost;                                    // ... and the host time it reaches the output
    /// <summary>Starting the engine takes its time the first time and must not
    /// hold the frame: it happens on this thread.</summary>
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private double _outputLatency, _ioBuffer;
    private int _tickLogs = 3;

    public IosClickPlayer()
    {
        _tick = MetronomeClick.Render(MetronomeClick.TickHz, 0.95f);
        _accent = MetronomeClick.Render(MetronomeClick.AccentHz, 1.0f);
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, 1, false);
        _render = Render;
        _source = new AVAudioSourceNode(format, _render);
        var thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable())
                try { job(); } catch (Exception ex) { FileLog.Error("click", ex); }
        }) { IsBackground = true, Name = "metronome", Priority = ThreadPriority.Highest };
        thread.Start();
        lock (SharedAudioEngine.Gate)
        {
            SharedAudioEngine.Engine.AttachNode(_source);
            SharedAudioEngine.Engine.Connect(_source, SharedAudioEngine.Engine.MainMixerNode, format);
        }
        // The route decides the latency (speaker 10–20 ms, AirPods ~170 ms):
        // read again whenever it changes, and log the first ticks after it so
        // a session that moves from headphones to the speaker shows both.
        AVAudioSession.Notifications.ObserveRouteChange((_, e) => { RefreshLatency("route " + e.Reason); _tickLogs = 3; });
        RefreshLatency("start");
    }

    /// <summary>The render callback, on the audio thread: silence, the ticks
    /// that fall in this block, and the block's stamp for the stream clock.</summary>
    private unsafe int Render(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
    {
        int n = (int)frameCount;
        var buffer = outputData[0];
        var dst = new Span<float>((void*)buffer.Data, n);
        dst.Clear();
        bool silent = true;
        lock (_gate)
        {
            if ((timestamp.Flags & AudioTimeStamp.AtsFlags.HostTimeValid) != 0)
            {
                _clockHost = timestamp.HostTime;
                _clockFrame = _rendered;
            }
            long first = _rendered, last = _rendered + n;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                long end = p.Start + p.Samples.Length;
                if (end <= first) { _pending.RemoveAt(i); continue; }
                if (p.Start >= last) continue;
                for (long f = Math.Max(p.Start, first); f < Math.Min(end, last); f++)
                    dst[(int)(f - first)] += p.Samples[f - p.Start] * p.Volume;
                silent = false;
                if (end <= last) _pending.RemoveAt(i);
            }
            _rendered = last;
        }
        isSilence = silent;
        return 0;
    }

    private void RefreshLatency(string why)
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            Volatile.Write(ref _outputLatency, session.OutputLatency);
            Volatile.Write(ref _ioBuffer, session.IOBufferDuration);
            FileLog.Info($"click latency: output {session.OutputLatency * 1000:0} ms, io buffer {session.IOBufferDuration * 1000:0} ms ({why})");
        }
        catch (Exception ex) { FileLog.Error("click latency", ex); }
    }

    /// <summary>The engine running; the source renders from then on. Under the engine's gate.</summary>
    private void Ensure()
    {
        bool wasRunning = SharedAudioEngine.Engine.Running;
        if (SharedAudioEngine.Ensure() && !wasRunning) RefreshLatency("engine start");   // the session's numbers are final once it runs
    }

    public void Prepare() => _work.Add(() =>
    {
        lock (SharedAudioEngine.Gate) { try { Ensure(); } catch (Exception ex) { FileLog.Error("click prepare", ex); } }
    });

    public void Click(bool accent) => ClickAt(0, accent);

    public void ClickAt(double delaySeconds, bool accent)
    {
        var samples = accent ? _accent : _tick;
        float volume = (float)Math.Clamp(Volume, 0, 1);
        // The moment the tick is to be *heard*, as a host time; the stream must
        // carry it the output latency earlier.
        double outputLatency = Volatile.Read(ref _outputLatency);
        ulong targetHost = AVAudioTime.HostTimeForSeconds(CAAnimation.CurrentMediaTime() + delaySeconds - outputLatency);
        string placed;
        lock (_gate)
        {
            long start;
            if (_clockHost != 0)
            {
                // Frames from the latest block's first frame to the target, on the stream's own rate.
                double dt = AVAudioTime.SecondsForHostTime(targetHost) - AVAudioTime.SecondsForHostTime(_clockHost);
                start = _clockFrame + (long)Math.Round(dt * SampleRate);
                if (start < _rendered)
                {
                    placed = $"{(_rendered - start) * 1000.0 / SampleRate:0} ms late, written at the next frame";
                    start = _rendered;
                }
                else placed = $"written {(start - _rendered) * 1000.0 / SampleRate:0} ms past the rendered frame";
            }
            else
            {
                start = _rendered;                               // the engine has not rendered yet: the first block gets it
                placed = "no stream clock yet, written at the first frame";
            }
            _pending.Add(new Pending(start, samples, volume));
        }
        if (!SharedAudioEngine.Engine.Running) Prepare();
        if (_tickLogs > 0)
        {
            _tickLogs--;
            FileLog.Info($"click: {placed} (asked {delaySeconds * 1000:0} ms, output latency {outputLatency * 1000:0} ms)");
        }
    }

    /// <summary>Ticks not yet rendered are dropped; one already in the stream sounds.</summary>
    public void Cancel()
    {
        lock (_gate) _pending.Clear();
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        Cancel();
        lock (SharedAudioEngine.Gate)
        {
            try { SharedAudioEngine.Engine.DetachNode(_source); } catch { /* engine gone */ }
            _source.Dispose();
        }
    }
}

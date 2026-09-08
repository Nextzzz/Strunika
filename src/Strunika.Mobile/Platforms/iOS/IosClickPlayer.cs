using System.Runtime.InteropServices;
using AVFoundation;
using CoreAnimation;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The metronome for a YouTube song, on the shared AVAudioEngine: one player
/// node, the tick as a PCM buffer, each tick scheduled on the node's render
/// clock for its exact moment. (A file song does not come here at all — its
/// ticks are mixed into its own stream, see <see cref="ITickTrack"/>.)
/// <para>
/// The moment is fixed in <see cref="ClickAt"/>, on the caller's thread, as a
/// host time; the worker only converts it to a sample. Every earlier build
/// measured "what is left of the delay" on the worker and took the session's
/// buffer and output latency off it — on AirPods that is 173 ms, more than
/// the 120 ms lookahead, so every tick came out negative and was played at
/// once, 65 ms late (log of 2026-09-08: "scheduled in −73 ms (asked 106)").
/// The render clock already says when a sample reaches the output; only the
/// hardware's own output latency is taken off, and the lookahead grows with
/// <see cref="Latency"/> instead of being capped.
/// </para>
/// </summary>
public sealed class IosClickPlayer : IClickPlayer
{
    private const double SampleRate = MetronomeClick.SampleRate;

    public double Volume { get; set; } = 1.0;
    public double Latency => Volatile.Read(ref _outputLatency) + Volatile.Read(ref _ioBuffer);

    private readonly AVAudioPlayerNode _node = new();
    private readonly AVAudioPcmBuffer _tick, _accent;
    /// <summary>The ticks' own thread: the pool's threads are busy with the
    /// network and the model while a song is analysed, and a queued tick once
    /// waited 175 ms for one.</summary>
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private double _outputLatency, _ioBuffer;
    private int _tickLogs = 3;

    public IosClickPlayer()
    {
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, 1, false);
        _tick = Buffer(format, MetronomeClick.Render(MetronomeClick.TickHz, 0.95f));
        _accent = Buffer(format, MetronomeClick.Render(MetronomeClick.AccentHz, 1.0f));
        var thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable())
                try { job(); } catch (Exception ex) { FileLog.Error("click", ex); }
        }) { IsBackground = true, Name = "metronome", Priority = ThreadPriority.Highest };
        thread.Start();
        lock (SharedAudioEngine.Gate)
        {
            SharedAudioEngine.Engine.AttachNode(_node);
            SharedAudioEngine.Engine.Connect(_node, SharedAudioEngine.Engine.MainMixerNode, format);
        }
        // The route decides the latency (speaker 10–20 ms, AirPods ~170 ms):
        // read again whenever it changes, and log the first ticks after it so
        // a session that moves from headphones to the speaker shows both.
        AVAudioSession.Notifications.ObserveRouteChange((_, e) => { RefreshLatency("route " + e.Reason); _tickLogs = 3; });
        // The engine stopped on its own (headphones in or out): the node is
        // played again by the next Ensure only if it knows it stopped.
        SharedAudioEngine.Stopped += () => { lock (SharedAudioEngine.Gate) { try { _node.Stop(); } catch { /* engine gone */ } } };
        RefreshLatency("start");
    }

    private static AVAudioPcmBuffer Buffer(AVAudioFormat format, float[] samples)
    {
        var buffer = new AVAudioPcmBuffer(format, (uint)samples.Length);
        // FloatChannelData is float** — one pointer per channel.
        var channel = Marshal.ReadIntPtr(buffer.FloatChannelData);
        Marshal.Copy(samples, 0, channel, samples.Length);
        buffer.FrameLength = (uint)samples.Length;
        return buffer;
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

    /// <summary>The engine running and the node playing; true when a tick can
    /// be scheduled. Under the engine's gate.</summary>
    private bool Ensure()
    {
        bool wasRunning = SharedAudioEngine.Engine.Running;
        if (!SharedAudioEngine.Ensure()) return false;
        if (!wasRunning) RefreshLatency("engine start");       // the session's numbers are final once it runs
        if (!_node.Playing) _node.Play();
        return true;
    }

    public void Prepare() => _work.Add(() =>
    {
        lock (SharedAudioEngine.Gate) { try { Ensure(); } catch (Exception ex) { FileLog.Error("click prepare", ex); } }
    });

    public void Click(bool accent) => ClickAt(0, accent);

    public void ClickAt(double delaySeconds, bool accent)
    {
        var buffer = accent ? _accent : _tick;
        float volume = (float)Math.Clamp(Volume, 0, 1);
        // The moment the tick is to be *heard*, as a host time; it is rendered
        // the output latency earlier. Fixed here, so the worker's own delay,
        // whatever it is, does not move the tick.
        double outputLatency = Volatile.Read(ref _outputLatency);
        ulong targetHost = AVAudioTime.HostTimeForSeconds(CAAnimation.CurrentMediaTime() + delaySeconds - outputLatency);
        _work.Add(() => Place(buffer, targetHost, volume, delaySeconds, outputLatency));
    }

    private void Place(AVAudioPcmBuffer buffer, ulong targetHost, float volume, double asked, double outputLatency)
    {
        lock (SharedAudioEngine.Gate)
        {
            try
            {
                if (!Ensure()) return;
                _node.Volume = volume;
                // The node's render clock pairs a sample with the host time it
                // reaches the output; a host time becomes the sample rendered
                // then, and that becomes a time in the player's own timeline
                // (samples since its Play), which is what ScheduleBuffer takes.
                AVAudioTime? at = null;
                double ahead = double.NaN;
                var nodeNow = _node.LastRenderTime;
                if (nodeNow is { HostTimeValid: true, SampleTimeValid: true })
                {
                    var target = new AVAudioTime(targetHost).ExtrapolateTimeFromAnchor(nodeNow);
                    var playerAt = target is { SampleTimeValid: true } ? _node.GetPlayerTimeFromNodeTime(target) : null;
                    var playerNow = _node.GetPlayerTimeFromNodeTime(nodeNow);
                    if (playerAt is { SampleTimeValid: true } && playerNow is { SampleTimeValid: true })
                    {
                        double rate = playerAt.SampleRate > 0 ? playerAt.SampleRate : SampleRate;
                        ahead = (playerAt.SampleTime - playerNow.SampleTime) / rate;
                        if (ahead > 0.002) at = playerAt;         // otherwise the sample is rendered already: play at once
                    }
                }
                _node.ScheduleBuffer(buffer, at, AVAudioPlayerNodeBufferOptions.Interrupts, null);
                if (_tickLogs > 0)
                {
                    _tickLogs--;
                    string placed = at != null ? $"placed {ahead * 1000:0} ms ahead on the render clock"
                                  : double.IsNaN(ahead) ? "no render clock yet, played at once"
                                  : $"{-ahead * 1000:0} ms late, played at once";
                    FileLog.Info($"click: {placed} (asked {asked * 1000:0} ms, output latency {outputLatency * 1000:0} ms)");
                }
            }
            catch (Exception ex) { FileLog.Error("click", ex); }
        }
    }

    /// <summary>Ticks still in the queue are dropped: stopping the node clears
    /// its schedule; it plays again from the next tick.</summary>
    public void Cancel()
    {
        lock (SharedAudioEngine.Gate)
        {
            try { if (_node.Playing) _node.Stop(); } catch { /* engine gone */ }
        }
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        lock (SharedAudioEngine.Gate)
        {
            try { _node.Stop(); SharedAudioEngine.Engine.DetachNode(_node); } catch { }
            _tick.Dispose();
            _accent.Dispose();
            _node.Dispose();
        }
    }
}

using System.Runtime.InteropServices;
using AVFoundation;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The metronome on an AVAudioEngine: one player node, the tick and the
/// accent as PCM buffers, each tick scheduled on the node's own sample
/// clock for its exact moment. This is how metronomes are built on iOS —
/// the buffer is placed at a sample, not started by a call. AVAudioPlayer
/// was tried first: its PlayAtTime needs the player prepared, the device
/// clock read at the right moment and a pool that is never busy, and after
/// two builds it still made no sound; the engine has none of that.
/// </summary>
public sealed class IosClickPlayer : IClickPlayer
{
    private const double SampleRate = MetronomeClick.SampleRate;

    public double Volume { get; set; } = 1.0;

    private readonly AVAudioEngine _engine = new();
    private readonly AVAudioPlayerNode _node = new();
    private readonly AVAudioPcmBuffer _tick, _accent;
    private readonly object _gate = new();
    private int _logged;
    /// <summary>The ticks' own thread. The thread pool was the wrong place:
    /// while a song is analysed the pool's threads are busy with the network
    /// and the model, and a queued tick waited 175 ms for one — behind the
    /// beat by the time it was placed (the log: "scheduled in −72 ms").</summary>
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();

    public IosClickPlayer()
    {
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, 1, false);
        _tick = Buffer(format, MetronomeClick.Render(1100, 0.95f));
        _accent = Buffer(format, MetronomeClick.Render(1650, 1.0f));
        var thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable())
                try { job(); } catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("click", ex); }
        }) { IsBackground = true, Name = "metronome", Priority = ThreadPriority.Highest };
        thread.Start();
        _engine.AttachNode(_node);
        _engine.Connect(_node, _engine.MainMixerNode, format);
        // A route change (headphones in or out) or an interruption stops the
        // engine; it is started again on the next tick (Ensure).
        AVAudioEngine.Notifications.ObserveConfigurationChange((_, _) => { lock (_gate) { try { _engine.Stop(); } catch { } } });
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

    /// <summary>What lies between a sample being rendered at its scheduled
    /// time and it being heard: the engine's IO buffer (the render runs one
    /// buffer ahead of the output) and the hardware's own output latency.
    /// Capped at 60 ms: with a video playing the session reported a buffer
    /// long enough to put every tick "in the past", so none was scheduled
    /// and all of them played at once, a frame's jitter apart; the lead under
    /// Expert settings absorbs what the numbers do not say.</summary>
    private double OutputLatency()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            double raw = session.OutputLatency + session.IOBufferDuration;
            if (_latencyLogged++ < 2)
                Strunika.Core.Diagnostics.FileLog.Info($"click latency: output {session.OutputLatency * 1000:0} ms, io buffer {session.IOBufferDuration * 1000:0} ms, node {_node.Latency * 1000:0} ms, presentation {_engine.OutputNode.PresentationLatency * 1000:0} ms");
            return Math.Min(raw, 0.06);
        }
        catch { return 0; }
    }

    private int _latencyLogged;

    /// <summary>The engine running and the node playing, on the mixable
    /// playback session; true when a tick can be scheduled.</summary>
    private bool Ensure()
    {
        AudioSessions.ForPlayback();
        if (!_engine.Running)
        {
            _engine.Prepare();
            if (!_engine.StartAndReturnError(out var error))
            {
                if (_logged++ < 3) Strunika.Core.Diagnostics.FileLog.Error("click: engine " + error?.LocalizedDescription);
                return false;
            }
        }
        if (!_node.Playing) _node.Play();
        return true;
    }

    public void Click(bool accent) => ClickAt(0, accent);

    public void ClickAt(double delaySeconds, bool accent)
    {
        var buffer = accent ? _accent : _tick;
        float volume = (float)Math.Clamp(Volume, 0, 1);
        long asked = System.Diagnostics.Stopwatch.GetTimestamp();
        // Off the frame: starting an engine is slow the first time, and the
        // conveyor must not wait for it.
        _work.Add(() =>
        {
            lock (_gate)
            {
                try
                {
                    if (!Ensure()) return;
                    _node.Volume = volume;
                    // The session's output latency (what the hardware adds after the
                    // engine renders) comes off the delay, so the tick is heard, not
                    // merely rendered, on the beat.
                    double remaining = delaySeconds - System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalSeconds - OutputLatency();
                    // A schedule time is in the player's own timeline (samples since
                    // its Play), not the node's render clock: the render clock is
                    // host samples in the billions, and a tick placed there was a
                    // day away — the first build made no sound. LastRenderTime is
                    // converted with PlayerTimeForNodeTime.
                    var nodeNow = _node.LastRenderTime;
                    var playerNow = nodeNow != null && nodeNow.SampleTimeValid ? _node.GetPlayerTimeFromNodeTime(nodeNow) : null;
                    AVAudioTime? at = null;
                    if (remaining > 0.002 && playerNow != null && playerNow.SampleTimeValid)
                    {
                        double rate = playerNow.SampleRate > 0 ? playerNow.SampleRate : SampleRate;
                        at = new AVAudioTime(playerNow.SampleTime + (long)(remaining * rate), rate);
                    }
                    _node.ScheduleBuffer(buffer, at, AVAudioPlayerNodeBufferOptions.Interrupts, null);
                    if (_logged < 3)
                    {
                        _logged++;
                        Strunika.Core.Diagnostics.FileLog.Info($"click: scheduled in {remaining * 1000:0} ms (asked {delaySeconds * 1000:0}), player time {(playerNow?.SampleTimeValid == true ? $"{playerNow.SampleTime} @ {playerNow.SampleRate:0}" : "invalid")}, engine {_engine.Running}, node {_node.Playing}");
                    }
                }
                catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("click", ex); }
            }
        });
    }

    /// <summary>Ticks still in the queue are dropped: stopping the node clears
    /// its schedule; it plays again from the next tick.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            try { if (_node.Playing) _node.Stop(); } catch { /* engine gone */ }
        }
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        lock (_gate)
        {
            try { _node.Stop(); _engine.Stop(); } catch { }
            _tick.Dispose();
            _accent.Dispose();
            _node.Dispose();
            _engine.Dispose();
        }
    }
}

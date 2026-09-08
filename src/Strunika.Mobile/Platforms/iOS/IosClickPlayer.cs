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

    public IosClickPlayer()
    {
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, 1, false);
        _tick = Buffer(format, MetronomeClick.Render(1100, 0.95f));
        _accent = Buffer(format, MetronomeClick.Render(1650, 1.0f));
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
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate)
            {
                try
                {
                    if (!Ensure()) return;
                    _node.Volume = volume;
                    double remaining = delaySeconds - System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalSeconds;
                    var now = _node.LastRenderTime;
                    AVAudioTime? at = null;
                    if (remaining > 0.002 && now != null && now.SampleTimeValid)
                    {
                        // The node's clock is in host samples: its own sample rate, not the buffer's.
                        double rate = now.SampleRate > 0 ? now.SampleRate : SampleRate;
                        at = new AVAudioTime(now.SampleTime + (long)(remaining * rate), rate);
                    }
                    _node.ScheduleBuffer(buffer, at, AVAudioPlayerNodeBufferOptions.Interrupts, null);
                    if (_logged < 3)
                    {
                        _logged++;
                        Strunika.Core.Diagnostics.FileLog.Info($"click: scheduled in {remaining * 1000:0} ms (asked {delaySeconds * 1000:0}), clock {(now?.SampleTimeValid == true ? now.SampleTime.ToString() : "invalid")}, engine {_engine.Running}, node {_node.Playing}");
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

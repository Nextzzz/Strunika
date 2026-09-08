using AVFoundation;
using Strunika.Core.Diagnostics;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The one AVAudioEngine our sounds share. The metronome's node and the song
/// player's chain hang off its mixer, so a tick and the song leave through
/// the same output on the same clock; everything that touches the graph or
/// a node does so under <see cref="Gate"/>.
/// </summary>
internal static class SharedAudioEngine
{
    public static readonly AVAudioEngine Engine = new();
    public static readonly object Gate = new();
    private static int _startsLogged;

    /// <summary>The engine stopped on its own: headphones in or out, a route
    /// or sample-rate change. Nodes have to be played again; the song player
    /// treats it as a pause, the way every iOS player does.</summary>
    public static event Action? Stopped;

    static SharedAudioEngine()
    {
        AVAudioEngine.Notifications.ObserveConfigurationChange((_, _) =>
        {
            lock (Gate) { try { Engine.Stop(); } catch { /* already gone */ } }
            FileLog.Info("audio engine: configuration changed, stopped");
            Stopped?.Invoke();                                   // outside the gate: listeners take it themselves
        });
        // A call or another app's audio stops the engine as well, without a
        // configuration change: the song pauses where it was, like every player.
        AVAudioSession.Notifications.ObserveInterruption((_, e) =>
        {
            if (e.InterruptionType != AVAudioSessionInterruptionType.Began) return;
            lock (Gate) { try { Engine.Stop(); } catch { /* already gone */ } }
            FileLog.Info("audio engine: interrupted, stopped");
            Stopped?.Invoke();
        });
    }

    /// <summary>Running, on the mixable playback session. Call under <see cref="Gate"/>.</summary>
    public static bool Ensure()
    {
        AudioSessions.ForPlayback();
        if (Engine.Running) return true;
        Engine.Prepare();
        if (!Engine.StartAndReturnError(out var error))
        {
            if (_startsLogged++ < 3) FileLog.Error("audio engine: " + error?.LocalizedDescription);
            return false;
        }
        return true;
    }
}

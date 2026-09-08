using AVFoundation;
using Strunika.Core.Diagnostics;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The one AVAudioEngine our sounds share. The metronome's source node and the
/// song's chain hang off its mixer, so a tick and the song leave through the
/// same output on the same clock; everything that touches the graph or a node
/// does so under <see cref="Gate"/>.
/// <para>The song's nodes are attached once and kept for the life of the app:
/// there is only ever one song page, and detaching nodes from a running
/// engine on every exit is where iOS crashed (2026-09-09).</para>
/// </summary>
internal static class SharedAudioEngine
{
    public static readonly AVAudioEngine Engine = new();
    public static readonly object Gate = new();
    /// <summary>The song's player and its pitch-preserving speed.</summary>
    public static readonly AVAudioPlayerNode SongNode = new();
    public static readonly AVAudioUnitTimePitch SongPitch = new();
    private static AVAudioFormat? _songFormat;
    private static int _startsLogged;

    /// <summary>The engine stopped on its own: headphones in or out, a route
    /// or sample-rate change, a call. Nodes have to be played again; the song
    /// player treats it as a pause, the way every iOS player does.</summary>
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

    /// <summary>The song chain connected for a file's format: attached the
    /// first time, reconnected only when the rate or the channel count differs
    /// from the file before. Call under <see cref="Gate"/>.</summary>
    public static void ConnectSong(AVAudioFormat format)
    {
        // (AVAudioFormat overloads ==, so the null checks are pattern matches.)
        if (_songFormat is not null && _songFormat.SampleRate == format.SampleRate && _songFormat.ChannelCount == format.ChannelCount) return;
        if (_songFormat is null)
        {
            Engine.AttachNode(SongNode);
            Engine.AttachNode(SongPitch);
        }
        Engine.Connect(SongNode, SongPitch, format);
        Engine.Connect(SongPitch, Engine.MainMixerNode, format);
        _songFormat = format;
        FileLog.Info($"audio engine: song chain at {format.SampleRate:0} Hz, {format.ChannelCount} ch");
    }
}

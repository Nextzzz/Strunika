using AVFoundation;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The one place the app's audio session is put into a playback state. Our
/// sounds — the metronome, a strummed chord, a song from a file — play as a
/// mixable Playback session: activating a non-mixable one (or a Record one
/// left over from the tuner) interrupts every other session on the device,
/// and the YouTube video, which plays in WebKit's own process, paused on
/// every metronome tick and whenever the "More" sheet came up. Mixable
/// sessions interrupt nothing.
/// </summary>
public static class AudioSessions
{
    private static bool _playback, _watching;
    private static readonly object Gate = new();

    /// <summary>Playback, mixable, active. Cheap to call before every sound:
    /// it only touches the session when the category is not already ours
    /// (the tuner's Record, the greeting's Ambient). Called from the tick's
    /// worker thread too, hence the gate.</summary>
    public static void ForPlayback()
    {
        lock (Gate)
        {
            try
            {
                Watch();
                var session = AVAudioSession.SharedInstance();
                bool ours = session.Category == AVAudioSession.CategoryPlayback
                            && session.CategoryOptions.HasFlag(AVAudioSessionCategoryOptions.MixWithOthers);
                if (!ours)
                {
                    Strunika.Core.Diagnostics.FileLog.Info($"audio session: {session.Category} → playback, mixable");
                    session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers);
                    _playback = false;
                }
                if (!_playback)
                {
                    session.SetActive(true);
                    _playback = true;
                }
            }
            catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("audio session", ex); }
        }
    }

    /// <summary>Interruptions are logged with their reason: the song's sound
    /// dropping out for a second on the phone needs a cause, not a guess.</summary>
    private static void Watch()
    {
        if (_watching) return;
        _watching = true;
        AVAudioSession.Notifications.ObserveInterruption((_, e) =>
        {
            Strunika.Core.Diagnostics.FileLog.Info($"audio session interruption: {e.InterruptionType} reason {e.Notification.UserInfo?[new Foundation.NSString("AVAudioSessionInterruptionReasonKey")]} options {e.Option}");
            // An interruption deactivates the session: the next sound activates it again.
            lock (Gate) _playback = false;
        });
        AVAudioSession.Notifications.ObserveRouteChange((_, e) =>
            Strunika.Core.Diagnostics.FileLog.Info($"audio route change: {e.Reason}"));
    }

    /// <summary>Something else set the category (the tuner took the microphone).</summary>
    public static void Changed() => _playback = false;
}

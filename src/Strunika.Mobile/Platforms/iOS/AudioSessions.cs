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
    private static bool _playback;

    /// <summary>Playback, mixable, active. Cheap to call before every sound:
    /// it only touches the session when the category is not already ours
    /// (the tuner's Record, the greeting's Ambient).</summary>
    public static void ForPlayback()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            bool ours = session.Category == AVAudioSession.CategoryPlayback
                        && session.CategoryOptions.HasFlag(AVAudioSessionCategoryOptions.MixWithOthers);
            if (!ours)
            {
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

    /// <summary>Something else set the category (the tuner took the microphone).</summary>
    public static void Changed() => _playback = false;
}

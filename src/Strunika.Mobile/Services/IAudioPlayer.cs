namespace Strunika.Mobile.Services;

/// <summary>
/// Plays a local song file (imports and recordings). Position is polled by
/// the song view-model (~5×/s), so there is no event traffic. Rate is
/// pitch-preserving where the platform supports it (iOS); the Windows head
/// ignores it.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    Task LoadAsync(string path);
    void Play();
    void Pause();
    bool IsPlaying { get; }
    double Duration { get; }
    /// <summary>Song seconds being heard now (not merely read from the file
    /// or handed to the output): the conveyor and the beat squares follow it.</summary>
    double Position { get; set; }
    /// <summary>0.5–1.25; 1 = normal.</summary>
    double Rate { get; set; }
    /// <summary>0–1.</summary>
    double Volume { get; set; }
}

/// <summary>
/// A player that mixes the metronome into its own stream: every tick is
/// written at the beat's sample, next to the song's own samples, so it stays
/// on the beat through any buffer, route latency or speed change — there is
/// nothing to place on a clock and nothing to calibrate. Both file players
/// do this; the YouTube embed cannot (its audio is WebKit's), so its ticks
/// are placed on the device clock by <see cref="IClickPlayer"/>.
/// </summary>
public interface ITickTrack
{
    /// <param name="beats">Beat times in song seconds, ascending.</param>
    /// <param name="enabled">Off: the stream carries only the song.</param>
    /// <param name="volume">0–1, independent of the song's volume.</param>
    void SetTicks(double[] beats, bool enabled, double volume);
}

/// <summary>Short metronome click, low latency, overlapping plays allowed.</summary>
public interface IClickPlayer : IDisposable
{
    /// <param name="accent">true on the first beat of a bar (higher, louder).</param>
    void Click(bool accent);
    /// <summary>A tick heard <paramref name="delaySeconds"/> from now, placed on
    /// the audio clock rather than fired when a frame notices the beat has passed —
    /// that was late by a frame plus the player's start-up, plainly behind the
    /// beat squares. The player takes its own <see cref="Latency"/> off, so the
    /// tick is heard, not merely rendered, at the asked moment. Zero or less
    /// plays at once.</summary>
    void ClickAt(double delaySeconds, bool accent);
    /// <summary>Ticks scheduled but not yet heard are dropped (pause, seek, a
    /// corrected position).</summary>
    void Cancel();
    /// <summary>Starts the output ahead of the first tick: an audio engine takes
    /// its time the first time, and a tick that had to start it was 200 ms late.</summary>
    void Prepare();
    /// <summary>Seconds between a tick being rendered and heard on the current
    /// route — 10–20 ms on a speaker, 160–250 ms on Bluetooth. The scheduler
    /// must ask for ticks at least this far ahead, or they cannot be placed.</summary>
    double Latency { get; }
    /// <summary>0–1, independent of the song's volume.</summary>
    double Volume { get; set; }
}

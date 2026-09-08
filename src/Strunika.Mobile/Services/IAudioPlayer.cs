namespace Strunika.Mobile.Services;

/// <summary>
/// Plays a local song file (imports and recordings). Position is polled by
/// the song view-model (100 ms), so there is no event traffic. Rate is
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
    double Position { get; set; }
    /// <summary>0.5–1.25; 1 = normal.</summary>
    double Rate { get; set; }
    /// <summary>0–1.</summary>
    double Volume { get; set; }
}

/// <summary>Short metronome click, low latency, overlapping plays allowed.</summary>
public interface IClickPlayer : IDisposable
{
    /// <param name="accent">true on the first beat of a bar (higher, louder).</param>
    void Click(bool accent);
    /// <summary>A tick <paramref name="delaySeconds"/> from now, placed on the
    /// audio clock rather than fired when a frame notices the beat has passed —
    /// that was late by a frame plus the player's start-up, plainly behind the
    /// beat squares. Zero or less plays at once.</summary>
    void ClickAt(double delaySeconds, bool accent);
    /// <summary>Ticks scheduled but not yet heard are dropped (pause, seek).</summary>
    void Cancel();
    /// <summary>0–1, independent of the song's volume.</summary>
    double Volume { get; set; }
}

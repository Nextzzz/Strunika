namespace Strunika.Mobile.Services;

/// <summary>
/// Plays a chord shape so the player can hear what a position sounds like
/// before choosing it. The sound is synthesized (<see cref="Strunika.Core.Audio.PluckSynth"/>),
/// so there is no sample library to ship or license.
/// </summary>
public interface IChordAudio : IDisposable
{
    /// <param name="frets">One entry per string from the low E up: −1 muted,
    /// 0 open, otherwise the fret — the capo already added.</param>
    void Strum(IReadOnlyList<int> frets);

    /// <summary>Cut the ringing chord short.</summary>
    void Stop();

    /// <summary>Raised on the main thread when the chord has finished ringing
    /// (or was stopped), so a play button can go back to its resting state.</summary>
    event EventHandler? Finished;

    /// <summary>0–1.</summary>
    double Volume { get; set; }
}

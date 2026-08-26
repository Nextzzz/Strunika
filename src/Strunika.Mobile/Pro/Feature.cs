namespace Strunika.Mobile.Pro;

/// <summary>
/// Everything that sits behind Strunika Pro. UI asks only
/// <c>IProGate.Has(Feature.X)</c>; where the entitlement came from
/// (StoreKit, an offer code, a dev override) is not the UI's business.
/// </summary>
public enum Feature
{
    /// <summary>Tunings other than Standard.</summary>
    AltTunings,
    /// <summary>A4 reference 430–450 Hz.</summary>
    A4Reference,
    /// <summary>Transpose + capo on the song page.</summary>
    TransposeCapo,
    /// <summary>Playback speed 0.5×–1.25×.</summary>
    Speed,
    /// <summary>A–B loop.</summary>
    ABLoop,
    /// <summary>TXT / PDF / XLSX export and share.</summary>
    Export,
    /// <summary>Chord Editor beyond the 3 free songs.</summary>
    ChordEditor,
    /// <summary>Folders / setlists in the library.</summary>
    Folders,
    /// <summary>Turning "simple chords" OFF on the live page.</summary>
    FullChordVocabulary,
    /// <summary>Song analyses beyond 20 lifetime + 1/day.</summary>
    UnlimitedSongs,
}

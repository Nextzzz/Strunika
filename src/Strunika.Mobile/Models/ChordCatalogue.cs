namespace Strunika.Mobile.Models;

/// <summary>
/// Every chord the app is willing to name, as roots × qualities. The chord
/// dictionary lists them and the editor offers them, and both have to offer
/// the same set — a chord a reader can look up but not choose would be a
/// puzzle. "Simple" keeps the qualities a beginner meets first; it is the
/// same setting as everywhere else (<c>AppSettings.SimpleChords</c>).
/// </summary>
public static class ChordCatalogue
{
    /// <summary>Roots as the recogniser names them, alphabetically from A.</summary>
    public static readonly string[] Roots = { "A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#" };

    /// <summary>Qualities, plainest first; "" is the major triad.</summary>
    public static readonly string[] Qualities = { "", "m", "7", "m7", "maj7", "6", "m6", "sus2", "sus4", "dim", "dim7", "m7b5", "aug", "mmaj7" };

    public static readonly string[] SimpleQualities = { "", "m", "dim", "aug" };

    public static string[] QualitiesFor(bool simple) => simple ? SimpleQualities : Qualities;
}

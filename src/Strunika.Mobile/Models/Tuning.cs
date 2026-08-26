using Strunika.Core.Analysis;
using Strunika.Mobile.Localization;

namespace Strunika.Mobile.Models;

/// <summary>
/// A tuning preset: string pitches as MIDI numbers, low to high. Only
/// Standard is free; the rest sit behind <c>Feature.AltTunings</c>.
/// </summary>
public sealed record Tuning(string Id, string NameKey, int[] Midi, bool IsPro, bool Flats = false)
{
    public static readonly Tuning Standard = new("standard", "Tuner_Standard", new[] { 40, 45, 50, 55, 59, 64 }, false);

    public static readonly IReadOnlyList<Tuning> All = new[]
    {
        Standard,
        new Tuning("drop_d", "Tuning_DropD", new[] { 38, 45, 50, 55, 59, 64 }, true),
        new Tuning("half_down", "Tuning_HalfDown", new[] { 39, 44, 49, 54, 58, 63 }, true, Flats: true),
        new Tuning("full_down", "Tuning_FullDown", new[] { 38, 43, 48, 53, 57, 62 }, true),
        new Tuning("dadgad", "Tuning_DADGAD", new[] { 38, 45, 50, 55, 57, 62 }, true),
        new Tuning("open_g", "Tuning_OpenG", new[] { 38, 43, 50, 55, 59, 62 }, true),
        new Tuning("open_d", "Tuning_OpenD", new[] { 38, 45, 50, 54, 57, 62 }, true),
        new Tuning("ukulele", "Tuning_Ukulele", new[] { 67, 60, 64, 69 }, true),
        new Tuning("bass", "Tuning_Bass", new[] { 28, 33, 38, 43 }, true),
    };

    public static Tuning ById(string id) => All.FirstOrDefault(t => t.Id == id) ?? Standard;

    public string Name => Loc.Get(NameKey);

    private static readonly string[] FlatNames = { "C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B" };

    public static string NoteName(int midi, bool flats = false) =>
        (flats ? FlatNames : Notes.Names)[((midi % 12) + 12) % 12].Replace("#", "♯");

    public string NoteName(int index) => NoteName(Midi[index], Flats);

    public static int Octave(int midi) => midi / 12 - 1;

    /// <summary>"E A D G B E" — the caption under a tuning's name.</summary>
    public string StringsCaption => string.Join(" ", Midi.Select(m => NoteName(m, Flats)));

    /// <summary>Octave subscript only where the same note name repeats in the
    /// tuning (E₂ … E₄); single occurrences show just the letter.</summary>
    public string Subscript(int index)
    {
        string name = NoteName(Midi[index], Flats);
        return Midi.Count(m => NoteName(m, Flats) == name) > 1 ? Octave(Midi[index]).ToString() : "";
    }
}

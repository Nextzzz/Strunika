namespace Strunika.Mobile.Models;

/// <summary>
/// Where things fall against a song's analysed beats — the one set of rules the
/// editor places chords by, whichever view of the song it is working in.
/// </summary>
public static class BeatMath
{
    /// <summary>The parts a beat is cut into on the track: sixteenths in x/4
    /// (user rule 2026-09-14).</summary>
    public const int Sixteenths = 4;

    /// <summary>
    /// The beat nearest a moment (the earlier one on a tie), −1 with no beats.
    /// This is what "the square a chord starts on" means: the beat grid draws
    /// the name there and the editor chooses by it, so a square that shows no
    /// chord never chooses one (user report 2026-09-14).
    /// </summary>
    public static int Nearest(double[] beats, double time)
    {
        if (beats.Length == 0) return -1;
        int i = Array.BinarySearch(beats, time);
        if (i >= 0) return i;
        i = ~i;
        if (i <= 0) return 0;
        if (i >= beats.Length) return beats.Length - 1;
        return beats[i] - time < time - beats[i - 1] ? i : i - 1;
    }

    /// <summary>The grid point nearest a moment: the gap between two beats cut
    /// into <paramref name="division"/>, carried on at the nearest gap's pace
    /// before the first beat and after the last. Without beats, the moment itself.</summary>
    public static double Snap(double[] beats, double time, int division = Sixteenths)
    {
        if (beats.Length < 2 || division < 1) return time;
        var (origin, unit) = Cell(beats, time, division);
        return origin + Math.Round((time - origin) / unit) * unit;
    }

    /// <summary>The next grid point past a moment, later for a positive
    /// <paramref name="direction"/> and earlier otherwise — what the editor's
    /// arrows step by. Without beats, an eighth of a second.</summary>
    public static double Step(double[] beats, double time, int direction, int division = Sixteenths)
    {
        int sign = direction >= 0 ? 1 : -1;
        if (beats.Length < 2 || division < 1) return time + sign * 0.125;
        // Just past the moment, so a moment already on the grid steps off it.
        double probe = time + sign * 1e-4;
        var (origin, unit) = Cell(beats, probe, division);
        double steps = (probe - origin) / unit;
        return origin + (sign > 0 ? Math.Ceiling(steps) : Math.Floor(steps)) * unit;
    }

    /// <summary>The time at a fractional beat index — 2.5 is half-way from the
    /// third beat to the fourth — carried on past either end at the nearest
    /// gap's pace. Rows of squares are whole beats, so a part of a row is a part
    /// of its beats.</summary>
    public static double TimeAt(double[] beats, double index)
    {
        if (beats.Length == 0) return 0;
        if (beats.Length == 1) return beats[0];
        int k = Math.Clamp((int)Math.Floor(index), 0, beats.Length - 2);
        return beats[k] + (index - k) * (beats[k + 1] - beats[k]);
    }

    /// <summary>The beat a moment's grid is counted from, and one part of its gap.</summary>
    private static (double Origin, double Unit) Cell(double[] beats, double time, int division)
    {
        int k = Array.BinarySearch(beats, time);
        if (k < 0) k = ~k - 1;                                   // the beat at or before the moment
        k = Math.Clamp(k, 0, beats.Length - 2);
        return (beats[k], Math.Max(1e-3, (beats[k + 1] - beats[k]) / division));
    }
}

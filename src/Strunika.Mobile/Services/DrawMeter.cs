namespace Strunika.Mobile.Services;

/// <summary>
/// What the canvases cost since it was last asked: every Draw on the song page
/// adds its time here, and the page's frame reads it when a frame ran long —
/// so a skipped frame can be put down to drawing, or not, from the log alone.
/// The UI thread only; two fields and a stopwatch stamp.
/// </summary>
public static class DrawMeter
{
    private static long _ticks;
    private static int _draws;

    public static long Begin() => System.Diagnostics.Stopwatch.GetTimestamp();

    public static void End(long began)
    {
        _ticks += System.Diagnostics.Stopwatch.GetTimestamp() - began;
        _draws++;
    }

    /// <summary>Milliseconds drawn and how many canvases, since the last call.</summary>
    public static (double Ms, int Draws) Take()
    {
        var taken = (_ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, _draws);
        _ticks = 0;
        _draws = 0;
        return taken;
    }
}

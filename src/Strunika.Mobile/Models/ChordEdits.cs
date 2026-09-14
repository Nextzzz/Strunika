using System.Globalization;

namespace Strunika.Mobile.Models;

/// <summary>
/// The editor's changes to a song's chords, as pure list operations: the song
/// stays gapless and in order whatever is done to it, and the rules can be run
/// on a list without a page or a player.
/// </summary>
public static class ChordEdits
{
    /// <summary>
    /// A chord's beginning moved anywhere in the song. A chord is the mark at
    /// the moment it starts and lasts until the next mark, so this is the one
    /// move there is: the chord is taken out of its place — the chord before it
    /// plays on through its time, as after a delete (the chord after, if it was
    /// first) — and its mark is set down at <paramref name="start"/>, where the
    /// chord lying there gives up the rest of its time to it. Set down on a
    /// mark, or too close to one for a chord of its own, it takes that chord's
    /// place. Returns the new list and where the moved chord is in it. A
    /// neighbour was a wall before, and a chord could not be dragged past the
    /// one before it (user report 2026-09-14).
    /// </summary>
    public static (List<ChordSegmentDto> Chords, int At) Move(IReadOnlyList<ChordSegmentDto> chords, int index, double start, double duration, double min)
    {
        var moved = chords[index];
        var rest = new List<ChordSegmentDto>(chords);
        if (index > 0) rest[index - 1] = rest[index - 1] with { End = moved.End };
        else if (rest.Count > 1) rest[index + 1] = rest[index + 1] with { Start = moved.Start };
        rest.RemoveAt(index);

        duration = Math.Max(duration, chords[^1].End);
        start = Math.Clamp(start, 0, Math.Max(0, duration - min));
        if (rest.Count == 0)
        {
            // The only chord: what it leaves before it is silence, not a gap.
            if (start < min) return (new List<ChordSegmentDto> { new(0, duration, moved.Label) }, 0);
            return (new List<ChordSegmentDto> { new(0, start, "—"), new(start, duration, moved.Label) }, 1);
        }
        int k = rest.FindIndex(c => c.Start <= start && start < c.End);
        if (k < 0) k = start < rest[0].Start ? 0 : rest.Count - 1;
        var cover = rest[k];
        bool whole = start - cover.Start < min                   // on its mark, or as good as
                     || (cover.End - start < min && cover.End - cover.Start < 2 * min);
        if (whole)
        {
            rest[k] = cover with { Label = moved.Label };
            return (rest, k);
        }
        if (cover.End - start < min) start = cover.End - min;    // room for a chord of its own before the next mark
        rest[k] = cover with { End = start };
        rest.Insert(k + 1, new ChordSegmentDto(start, cover.End, moved.Label));
        return (rest, k + 1);
    }

    /// <summary>For logs and checks: "C[0-2] G[2-4]".</summary>
    public static string Describe(IEnumerable<ChordSegmentDto> chords) =>
        string.Join(" ", chords.Select(c => string.Format(CultureInfo.InvariantCulture, "{0}[{1:0.##}-{2:0.##}]", c.Label, c.Start, c.End)));
}

namespace Strunika.Mobile.Models;

/// <summary>A guitar fingering: six strings low E → high e; -1 = muted, 0 = open,
/// otherwise the absolute fret. <see cref="BaseFret"/> is the first fret drawn
/// (1 for open position). <see cref="Barre"/> is the fret of a full barre or 0.</summary>
public sealed record ChordShape(string Chord, int[] Frets, int BaseFret, int Barre)
{
    /// <summary>"x 3 2 0 1 0" — the caption under a diagram.</summary>
    public string FretsText => string.Join(" ", Frets.Select(f => f < 0 ? "x" : f.ToString()));

    public int MaxFret => Frets.Where(f => f > 0).DefaultIfEmpty(0).Max();
}

/// <summary>
/// Chord → fingering, capo-aware. Open-position shapes for the C / G / D
/// families come from a small table; everything else is a movable E-form
/// (root on the 6th string) or A-form (root on the 5th) shape, whichever
/// sits lower on the neck. With a capo the shape is chosen for the chord
/// transposed down by the capo, so the diagram shows what the hand plays.
/// </summary>
public static class ChordShapes
{
    private static readonly string[] Sharps = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Open shapes: root pitch class + quality → frets (low → high).
    private static readonly Dictionary<(int Root, string Quality), int[]> Open = new()
    {
        [(0, "")] = new[] { -1, 3, 2, 0, 1, 0 },       // C
        [(0, "maj7")] = new[] { -1, 3, 2, 0, 0, 0 },
        [(0, "7")] = new[] { -1, 3, 2, 3, 1, 0 },
        [(0, "sus4")] = new[] { -1, 3, 3, 0, 1, 1 },
        [(0, "add9")] = new[] { -1, 3, 2, 0, 3, 0 },
        [(9, "")] = new[] { -1, 0, 2, 2, 2, 0 },       // A
        [(9, "m")] = new[] { -1, 0, 2, 2, 1, 0 },
        [(9, "7")] = new[] { -1, 0, 2, 0, 2, 0 },
        [(9, "m7")] = new[] { -1, 0, 2, 0, 1, 0 },
        [(9, "maj7")] = new[] { -1, 0, 2, 1, 2, 0 },
        [(9, "sus2")] = new[] { -1, 0, 2, 2, 0, 0 },
        [(9, "sus4")] = new[] { -1, 0, 2, 2, 3, 0 },
        [(7, "")] = new[] { 3, 2, 0, 0, 0, 3 },        // G
        [(7, "7")] = new[] { 3, 2, 0, 0, 0, 1 },
        [(7, "maj7")] = new[] { 3, 2, 0, 0, 0, 2 },
        [(7, "sus4")] = new[] { 3, 3, 0, 0, 1, 3 },
        [(4, "")] = new[] { 0, 2, 2, 1, 0, 0 },        // E
        [(4, "m")] = new[] { 0, 2, 2, 0, 0, 0 },
        [(4, "7")] = new[] { 0, 2, 0, 1, 0, 0 },
        [(4, "m7")] = new[] { 0, 2, 0, 0, 0, 0 },
        [(4, "maj7")] = new[] { 0, 2, 1, 1, 0, 0 },
        [(4, "sus4")] = new[] { 0, 2, 2, 2, 0, 0 },
        [(2, "")] = new[] { -1, -1, 0, 2, 3, 2 },      // D
        [(2, "m")] = new[] { -1, -1, 0, 2, 3, 1 },
        [(2, "7")] = new[] { -1, -1, 0, 2, 1, 2 },
        [(2, "m7")] = new[] { -1, -1, 0, 2, 1, 1 },
        [(2, "maj7")] = new[] { -1, -1, 0, 2, 2, 2 },
        [(2, "sus2")] = new[] { -1, -1, 0, 2, 3, 0 },
        [(2, "sus4")] = new[] { -1, -1, 0, 2, 3, 3 },
        [(5, "maj7")] = new[] { -1, -1, 3, 2, 1, 0 },  // Fmaj7
        [(11, "7")] = new[] { -1, 2, 1, 2, 0, 2 },     // B7
    };

    // Movable shapes as offsets from the root fret f (null = muted). E-form / A-form.
    private static readonly Dictionary<string, (int?[] E, int?[] A)> Movable = new()
    {
        [""] = (new int?[] { 0, 2, 2, 1, 0, 0 }, new int?[] { null, 0, 2, 2, 2, 0 }),
        ["m"] = (new int?[] { 0, 2, 2, 0, 0, 0 }, new int?[] { null, 0, 2, 2, 1, 0 }),
        ["7"] = (new int?[] { 0, 2, 0, 1, 0, 0 }, new int?[] { null, 0, 2, 0, 2, 0 }),
        ["m7"] = (new int?[] { 0, 2, 0, 0, 0, 0 }, new int?[] { null, 0, 2, 0, 1, 0 }),
        ["maj7"] = (new int?[] { 0, null, 1, 1, 0, null }, new int?[] { null, 0, 2, 1, 2, 0 }),
        ["sus4"] = (new int?[] { 0, 2, 2, 2, 0, 0 }, new int?[] { null, 0, 2, 2, 3, 0 }),
        ["sus2"] = (new int?[] { 0, 2, 4, 4, 0, 0 }, new int?[] { null, 0, 2, 2, 0, 0 }),
        ["dim"] = (new int?[] { 0, 1, 2, 0, null, null }, new int?[] { null, 0, 1, -1, 1, null }),
        ["dim7"] = (new int?[] { 0, null, 1, 0, 1, null }, new int?[] { null, 0, 1, -1, 1, null }),
        ["m7b5"] = (new int?[] { 0, null, 0, 0, 0, null }, new int?[] { null, 0, 1, 0, 1, null }),
        ["aug"] = (new int?[] { 0, 3, 2, 1, 1, null }, new int?[] { null, 0, 3, 2, 2, null }),
        ["6"] = (new int?[] { 0, 2, 2, 1, 2, 0 }, new int?[] { null, 0, 2, 2, 2, 2 }),
        ["m6"] = (new int?[] { 0, 2, 2, 0, 2, 0 }, new int?[] { null, 0, 2, 2, 1, 2 }),
        ["9"] = (new int?[] { 0, 2, 0, 1, 0, 2 }, new int?[] { null, 0, 2, 0, 0, 0 }),
        ["add9"] = (new int?[] { 0, 2, 2, 1, 0, 2 }, new int?[] { null, 0, 2, 2, 0, 0 }),
    };

    /// <summary>Root pitch class (0 = C) and quality suffix of a pretty label
    /// ("F#m7" → 6, "m7"; "B♭" → 10, ""). Null for "—" / N / X.</summary>
    public static (int Root, string Quality)? Parse(string pretty)
    {
        if (string.IsNullOrWhiteSpace(pretty) || pretty is "—" or "N" or "X") return null;
        var s = pretty.Replace('♯', '#').Replace('♭', 'b');
        int root = "C D EF G A B".IndexOf(char.ToUpperInvariant(s[0]));
        if (root < 0) return null;
        int i = 1;
        if (i < s.Length && s[i] == '#') { root++; i++; }
        else if (i < s.Length && s[i] == 'b') { root--; i++; }
        string quality = s[i..];
        int slash = quality.IndexOf('/');
        if (slash >= 0) quality = quality[..slash];          // slash bass: play the plain chord
        quality = quality switch { "min" => "m", "maj" => "", "M7" => "maj7", "min7" => "m7", "°" or "o" => "dim", "ø" => "m7b5", "+" => "aug", _ => quality };
        return ((root + 12) % 12, quality);
    }

    /// <summary>All reasonable positions for a chord, lowest first; empty when the label is not a chord.</summary>
    public static IReadOnlyList<ChordShape> Positions(string pretty, int capo = 0)
    {
        var parsed = Parse(pretty);
        if (parsed == null) return Array.Empty<ChordShape>();
        var (root, quality) = parsed.Value;
        int played = ((root - capo) % 12 + 12) % 12;
        var list = new List<ChordShape>();

        if (Open.TryGetValue((played, quality), out var open))
            list.Add(Build(pretty, open));

        if (!Movable.TryGetValue(quality, out var forms))
            forms = Movable[quality.StartsWith('m') && !quality.StartsWith("maj") ? "m" : ""];
        int fe = (played - 4 + 12) % 12, fa = (played - 9 + 12) % 12;
        foreach (var (offsets, f) in new[] { (forms.E, fe), (forms.A, fa) })
        {
            int frets = f;
            if (offsets.Any(o => o.HasValue && f + o.Value < 0)) frets += 12;   // dim shapes reach one fret below the root
            if (frets == 0 && list.Count > 0) continue;                         // open shape already listed
            var abs = offsets.Select(o => o.HasValue ? frets + o.Value : -1).ToArray();
            var shape = Build(pretty, abs, barre: frets > 0 && offsets.Count(o => o == 0) >= 2 ? frets : 0);
            if (shape.MaxFret <= 15 && !list.Any(s => s.Frets.SequenceEqual(shape.Frets)))
                list.Add(shape);
        }
        return list.OrderBy(s => s.BaseFret).ThenBy(s => s.MaxFret).ToList();
    }

    /// <summary>
    /// The capo that turns the most of these chords into open shapes — the
    /// "smart capo" a player looks for. Chords are weighted by how often they
    /// occur, an open shape scores 3 and a shape within the first three frets
    /// scores 1; ties go to the lower capo, and capo 0 keeps a small edge so a
    /// marginal gain does not send the player looking for a capo.
    /// </summary>
    public static int SuggestCapo(IEnumerable<string> labels, int maxCapo = 7)
    {
        var weights = new Dictionary<string, int>();
        foreach (var label in labels)
        {
            if (string.IsNullOrEmpty(label) || label == "—") continue;
            weights[label] = weights.TryGetValue(label, out var n) ? n + 1 : 1;
        }
        if (weights.Count == 0) return 0;

        int best = 0;
        double bestScore = double.MinValue;
        for (int capo = 0; capo <= maxCapo; capo++)
        {
            double score = capo == 0 ? 0.5 : 0;
            foreach (var (label, count) in weights)
            {
                var shape = For(label, capo);
                if (shape == null) continue;
                if (shape.BaseFret == 1 && shape.Barre == 0) score += 3.0 * count;
                else if (shape.BaseFret <= 3) score += 1.0 * count;
            }
            if (score > bestScore) { bestScore = score; best = capo; }
        }
        return best;
    }

    /// <summary>The preferred position (lowest on the neck).</summary>
    public static ChordShape? For(string pretty, int capo = 0) => Positions(pretty, capo).FirstOrDefault();

    private static ChordShape Build(string chord, int[] frets, int barre = 0)
    {
        int min = frets.Where(f => f > 0).DefaultIfEmpty(1).Min();
        int max = frets.Where(f => f > 0).DefaultIfEmpty(1).Max();
        int baseFret = max <= 4 ? 1 : min;
        return new ChordShape(chord, frets, baseFret, barre);
    }

    public static string RootName(int pitchClass) => Sharps[((pitchClass % 12) + 12) % 12];
}

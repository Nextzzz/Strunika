namespace Strunika.Core.Analysis;

/// <summary>
/// Pitch → fundamental → string, for a guitar tuner.
///
/// YIN alone is not enough for the low strings: right after a pluck the
/// fundamental of the low E is far weaker than its 2nd harmonic, so YIN
/// reports E3 (or E4) and the tuner jumps to the wrong string. This engine
/// takes YIN's candidate and asks whether the spectrum carries evidence of a
/// lower fundamental — energy at the sub-octave itself and, decisively, at
/// the odd multiples of it (1.5·f, 2.5·f…) that a true f could never produce.
/// Subharmonic candidates f/2, f/3, f/4 are tried from the lowest up and the
/// first one with enough evidence wins.
///
/// Strings are then chosen by real pitch distance (fret 3 on the low E is a
/// G2 and belongs to the A string), while the cents readout folds octaves so
/// a decaying note's harmonics keep reading against the chosen string.
/// </summary>
public sealed class TunerEngine
{
    private readonly PitchDetector _yin;
    private readonly int _sampleRate;
    private float[]? _hann;

    /// <summary>Evidence (sum of amplitudes at the sub-fundamental and its
    /// odd-relative harmonics, relative to the amplitude at YIN's pitch)
    /// needed to accept a sub-harmonic candidate as the fundamental. Leakage
    /// and noise sit around 0.05; a weak real fundamental around 0.2–0.4.</summary>
    public double SubharmonicThreshold { get; init; } = 0.2;

    public double MinFrequency { get; init; } = 35.0;

    public TunerEngine(int sampleRate)
    {
        _sampleRate = sampleRate;
        _yin = new PitchDetector { MinFrequency = 35.0 };
    }

    /// <summary>Raw YIN pitch with the octave corrected, or null when the
    /// window has no clear pitch. <paramref name="clarity"/> is YIN's
    /// confidence (1 − CMND at the chosen lag).</summary>
    public double? DetectFundamental(ReadOnlySpan<float> window, out double clarity)
    {
        clarity = 0;
        var yin = _yin.Detect(window, _sampleRate);
        if (yin == null)
            return null;
        clarity = yin.Value.Clarity;
        return CorrectOctave(window, yin.Value.Frequency);
    }

    /// <summary>The lowest sub-harmonic of <paramref name="frequency"/> that
    /// the spectrum supports; the frequency itself when none does.</summary>
    public double CorrectOctave(ReadOnlySpan<float> window, double frequency)
    {
        var hann = HannFor(window.Length);
        double baseFrequency = frequency;

        // Descend one octave (or a third) at a time: each step asks whether the
        // multiples of the lower candidate that the current base cannot
        // explain — the sub-fundamental and its odd-relative harmonics — carry
        // real amplitude. Two steps cover YIN landing on the 4th harmonic.
        for (int step = 0; step < 2; step++)
        {
            // Reference = the loudest partial the base explains (its fundamental
            // may itself be weak — that is the whole point).
            double reference = 0;
            for (int h = 1; h <= 4 && baseFrequency * h * 2 < _sampleRate; h++)
                reference = Math.Max(reference, Math.Sqrt(Goertzel(window, baseFrequency * h, hann)));
            if (reference <= 0)
                break;
            bool descended = false;
            foreach (int divisor in new[] { 2, 3 })
            {
                double candidate = baseFrequency / divisor;
                if (candidate < MinFrequency)
                    continue;
                double evidence = 0;
                for (int m = 1; m <= 4 * divisor; m++)
                {
                    if (m % divisor == 0) continue;               // explained by the base already
                    double freq = candidate * m;
                    if (freq * 2 > _sampleRate) break;
                    evidence += Math.Sqrt(Goertzel(window, freq, hann)) / reference;
                }
                if (evidence > SubharmonicThreshold)
                {
                    baseFrequency = candidate;
                    descended = true;
                    break;
                }
            }
            if (!descended)
                break;
        }
        return baseFrequency;
    }

    /// <summary>MIDI note number (fractional) of a frequency against A4.</summary>
    public static double MidiOf(double frequency, double a4 = 440.0) =>
        Notes.A4Midi + 12.0 * Math.Log2(frequency / a4);

    /// <summary>The string nearest in real pitch (not pitch class).</summary>
    public static int NearestString(double midi, IReadOnlyList<int> stringMidi)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < stringMidi.Count; i++)
        {
            double d = Math.Abs(midi - stringMidi[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Semitone offset from a string, folded into ±6 so a decaying
    /// note's octave harmonics read as the same string.</summary>
    public static double FoldedSemitones(double midi, int stringMidi)
    {
        double d = midi - stringMidi;
        d -= 12.0 * Math.Round(d / 12.0);
        return d;
    }

    private float[] HannFor(int length)
    {
        if (_hann == null || _hann.Length != length)
        {
            _hann = new float[length];
            for (int i = 0; i < length; i++)
                _hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (length - 1.0)));
        }
        return _hann;
    }

    /// <summary>Windowed Goertzel power at one frequency.</summary>
    private double Goertzel(ReadOnlySpan<float> x, double frequency, float[] hann)
    {
        double k = 2 * Math.Cos(2 * Math.PI * frequency / _sampleRate);
        double s1 = 0, s2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double s0 = x[i] * hann[i] + k * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        return s1 * s1 + s2 * s2 - k * s1 * s2;
    }
}

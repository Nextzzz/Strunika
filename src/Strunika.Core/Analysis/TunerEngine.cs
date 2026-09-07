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
/// <para>
/// Noise is the other enemy. Room noise is pink: most of its energy sits
/// low, and YIN reads long-lag correlations in it as a 35–55 Hz "pitch" with
/// a clarity of 0.9 — with the sub-harmonic search then happily descending
/// to it. So the search floor follows the tuning (a few semitones under its
/// lowest string: nothing below can be a string), the evidence for a
/// sub-harmonic is what stands above the local spectral background rather
/// than raw amplitude, and <see cref="HarmonicRatio"/> says how much of the
/// window's energy the pitch actually explains, for the caller to gate on.
/// </para>
/// </summary>
public sealed class TunerEngine
{
    private PitchDetector _yin;
    private readonly int _sampleRate;
    private float[]? _hann;
    private double _minFrequency;
    private double[]? _re, _im;

    /// <summary>Evidence (sum of amplitudes at the sub-fundamental and its
    /// odd-relative harmonics, relative to the amplitude at YIN's pitch)
    /// needed to accept a sub-harmonic candidate as the fundamental. Leakage
    /// and noise sit around 0.05; a weak real fundamental around 0.2–0.4.</summary>
    public double SubharmonicThreshold { get; init; } = 0.2;

    /// <summary>Lowest pitch worth finding, for YIN and for the sub-harmonic
    /// search alike. Set it from the tuning: a few semitones under the lowest
    /// string keeps the low-frequency rumble of a room out of the reading.</summary>
    public double MinFrequency
    {
        get => _minFrequency;
        set
        {
            if (Math.Abs(value - _minFrequency) < 0.01) return;
            _minFrequency = value;
            _yin = new PitchDetector { MinFrequency = value };
        }
    }

    public TunerEngine(int sampleRate, double minFrequency = 35.0)
    {
        _sampleRate = sampleRate;
        _minFrequency = minFrequency;
        _yin = new PitchDetector { MinFrequency = minFrequency };
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
                    // Only what rises above the spectrum around it counts: noise
                    // has energy at every frequency, a partial is a peak. The
                    // background is read halfway to the base's own partials on
                    // either side (they sit one candidate away) — unless that
                    // is inside a Hann main lobe, as on a bass string in a short
                    // window, where the spectrum is too crowded to read one.
                    double aside = candidate / 2;
                    double background = aside < 2.5 * _sampleRate / window.Length ? 0
                        : 0.5 * (Math.Sqrt(Goertzel(window, freq - aside, hann)) + Math.Sqrt(Goertzel(window, freq + aside, hann)));
                    evidence += Math.Max(0, Math.Sqrt(Goertzel(window, freq, hann)) - background) / reference;
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

    /// <summary>
    /// The share of the window's energy (Hann-windowed) that
    /// sits within ±2 bins of the first twelve harmonics of
    /// <paramref name="frequency"/>. A plucked string explains 0.3–0.9 of it;
    /// a pitch YIN found in noise explains far less. Near the floor of the
    /// search range harmonics crowd the bins, so the figure is only meaningful
    /// for guitar pitches — which is what the floor is there to guarantee.
    /// </summary>
    public double HarmonicRatio(ReadOnlySpan<float> window, double frequency)
    {
        int n = window.Length;
        if (n < 16 || frequency <= 0) return 0;
        if (_re == null || _re.Length != n) { _re = new double[n]; _im = new double[n]; }
        var re = _re; var im = _im!;
        var hann = HannFor(n);
        for (int i = 0; i < n; i++) { re[i] = window[i] * hann[i]; im[i] = 0; }
        Dsp.Fft.Forward(re, im);
        int half = n / 2;
        double total = 0;
        for (int k = 1; k < half; k++) total += re[k] * re[k] + im[k] * im[k];
        if (total <= 0) return 0;
        double binHz = (double)_sampleRate / n, harmonic = 0;
        int last = 0;
        for (int h = 1; h <= 12; h++)
        {
            int centre = (int)Math.Round(frequency * h / binHz);
            if (centre + 2 >= half) break;
            for (int k = Math.Max(Math.Max(1, last + 1), centre - 2); k <= centre + 2; k++)
                harmonic += re[k] * re[k] + im[k] * im[k];
            last = centre + 2;                                   // never count a bin twice
        }
        return harmonic / total;
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

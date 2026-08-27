using Strunika.Core.Analysis;

namespace Strunika.Core.Tests;

[TestFixture]
public class TunerEngineTests
{
    private static readonly int[] Standard = { 40, 45, 50, 55, 59, 64 };   // E2 A2 D3 G3 B3 E4

    /// <summary>A low guitar string right after the pluck: the even harmonics
    /// dominate, the fundamental and the odd harmonics are weak, so the wave
    /// is almost periodic at half the period and YIN reports the 2nd harmonic.</summary>
    private static float[] WeakFundamental(double f0, double seconds, double fundamental = 0.12)
    {
        double[] amps = { fundamental, 1.0, 0.12, 0.35, 0.06, 0.15 };
        int n = (int)(seconds * TestSignals.SampleRate);
        var x = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)TestSignals.SampleRate, v = 0;
            for (int h = 0; h < amps.Length; h++)
                v += amps[h] * Math.Sin(2 * Math.PI * f0 * (h + 1) * t + h);
            x[i] = (float)(0.3 * v);
        }
        return x;
    }

    private static double Cents(double detected, double expected) => 1200 * Math.Log2(detected / expected);

    [TestCase(82.41)]   // E2
    [TestCase(110.00)]  // A2
    [TestCase(146.83)]  // D3
    [TestCase(196.00)]  // G3
    [TestCase(246.94)]  // B3
    [TestCase(329.63)]  // E4
    public void DetectFundamental_KarplusPluck_FindsFundamentalEarly(double frequency)
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = TestSignals.Pluck(frequency, 1.0);

        // 60 ms after the attack — where a tuner first looks.
        var f = engine.DetectFundamental(samples.AsSpan(2646, 4096), out _);

        Assert.That(f, Is.Not.Null);
        Assert.That(Math.Abs(Cents(f!.Value, frequency)), Is.LessThan(60), $"detected {f:F2} Hz");
    }

    [TestCase(82.41)]
    [TestCase(110.00)]
    public void DetectFundamental_WeakFundamental_DoesNotJumpAnOctave(double frequency)
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = WeakFundamental(frequency, 0.5);

        var yin = new PitchDetector { MinFrequency = 35 }.Detect(samples.AsSpan(0, 4096), TestSignals.SampleRate);
        var f = engine.DetectFundamental(samples.AsSpan(0, 4096), out _);

        // Sanity: this is the case YIN gets wrong (it hears the 2nd harmonic).
        Assert.That(yin!.Value.Frequency, Is.GreaterThan(frequency * 1.8), "fixture should fool plain YIN");
        Assert.That(f, Is.Not.Null);
        Assert.That(Math.Abs(Cents(f!.Value, frequency)), Is.LessThan(20), $"detected {f:F2} Hz");
    }

    [TestCase(329.63)]  // E4 must not be pulled down to E2
    [TestCase(246.94)]
    public void DetectFundamental_PureSine_IsNotPulledToSubharmonic(double frequency)
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = TestSignals.Sine(frequency, 0.3);

        var f = engine.DetectFundamental(samples.AsSpan(0, 4096), out _);

        Assert.That(f, Is.Not.Null);
        Assert.That(Math.Abs(Cents(f!.Value, frequency)), Is.LessThan(5), $"detected {f:F2} Hz");
    }

    [Test]
    public void DetectFundamental_HighEPluck_StaysOnE4()
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = TestSignals.Pluck(329.63, 0.6);

        var f = engine.DetectFundamental(samples.AsSpan(2646, 4096), out _);

        Assert.That(Math.Abs(Cents(f!.Value, 329.63)), Is.LessThan(60), $"detected {f:F2} Hz");
    }

    // Alternative tunings: the engine is tuning-agnostic, but the extremes
    // deserve their own evidence — ukulele sits an octave above a guitar,
    // a bass an octave below (E1 = 41 Hz is under four periods per window).
    [TestCase(392.00)]  // ukulele G4 (re-entrant)
    [TestCase(261.63)]  // ukulele C4
    [TestCase(440.00)]  // ukulele A4
    [TestCase(41.20)]   // bass E1
    [TestCase(55.00)]   // bass A1
    [TestCase(73.42)]   // bass D2
    [TestCase(98.00)]   // bass G2
    public void DetectFundamental_UkuleleAndBassPlucks_FindTheFundamental(double frequency)
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = TestSignals.Pluck(frequency, 1.0);

        var f = engine.DetectFundamental(samples.AsSpan(2646, 4096), out _);

        Assert.That(f, Is.Not.Null);
        Assert.That(Math.Abs(Cents(f!.Value, frequency)), Is.LessThan(60), $"detected {f:F2} Hz");
    }

    [TestCase(41.20)]
    [TestCase(55.00)]
    public void DetectFundamental_BassWeakFundamental_DoesNotJumpAnOctave(double frequency)
    {
        var engine = new TunerEngine(TestSignals.SampleRate);
        var samples = WeakFundamental(frequency, 0.5);

        var f = engine.DetectFundamental(samples.AsSpan(0, 4096), out _);

        Assert.That(f, Is.Not.Null);
        Assert.That(Math.Abs(Cents(f!.Value, frequency)), Is.LessThan(30), $"detected {f:F2} Hz");
    }

    [Test]
    public void NearestString_Ukulele_ReentrantHighG()
    {
        int[] ukulele = { 67, 60, 64, 69 };   // G4 C4 E4 A4
        Assert.That(TunerEngine.NearestString(67.1, ukulele), Is.EqualTo(0), "G4 → the high-G string, not E or A");
        Assert.That(TunerEngine.NearestString(59.6, ukulele), Is.EqualTo(1), "flat C4 → C string");
        Assert.That(TunerEngine.NearestString(66.4, ukulele), Is.EqualTo(0), "between E4 and G4, closer to G");
    }

    [Test]
    public void NearestString_Bass_LowStrings()
    {
        int[] bass = { 28, 33, 38, 43 };   // E1 A1 D2 G2
        Assert.That(TunerEngine.NearestString(28.3, bass), Is.EqualTo(0));
        Assert.That(TunerEngine.NearestString(31.0, bass), Is.EqualTo(1), "fret 3 on E1 = G1 → the A string");
        Assert.That(TunerEngine.FoldedSemitones(40.1, 28), Is.EqualTo(0.1).Within(1e-9), "E2 harmonic reads against E1");
    }

    [Test]
    public void NearestString_FrettedNotesOnLowE_GoToTheNearestString()
    {
        // Fret 2 = F#2 (midi 42) → still the E string; fret 3 = G2 (43) → the A string.
        Assert.That(TunerEngine.NearestString(42, Standard), Is.EqualTo(0));
        Assert.That(TunerEngine.NearestString(43, Standard), Is.EqualTo(1));
        // Real pitch, not pitch class: G2 is not the G string.
        Assert.That(TunerEngine.NearestString(43.1, Standard), Is.Not.EqualTo(3));
    }

    [Test]
    public void FoldedSemitones_HarmonicOfChosenString_ReadsAsInTune()
    {
        // E2's 2nd harmonic (E3, midi 52) against the E2 string (40) folds to 0.
        Assert.That(TunerEngine.FoldedSemitones(52.05, 40), Is.EqualTo(0.05).Within(1e-9));
        // 30 cents flat stays 30 cents flat.
        Assert.That(TunerEngine.FoldedSemitones(39.7, 40), Is.EqualTo(-0.3).Within(1e-9));
    }

    [Test]
    public void MidiOf_A4_Is69AtAnyReference()
    {
        Assert.That(TunerEngine.MidiOf(440), Is.EqualTo(69).Within(1e-9));
        Assert.That(TunerEngine.MidiOf(432, a4: 432), Is.EqualTo(69).Within(1e-9));
    }
}

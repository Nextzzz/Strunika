namespace Strunika.Mobile.Services;

/// <summary>A short metronome tick synthesized on the fly (no asset): a damped
/// tone with a hard attack, a little of its octave and a touch of soft
/// saturation, 90 ms. The peak is full scale already (user: "louder, several
/// times", 2026-09-07), so loudness comes from energy, not amplitude: a slow
/// decay and the saturation lift the RMS of the attack by ~5 dB and the energy
/// by ~7 dB over the first 45 ms tick at the same peak (user: "louder",
/// 2026-09-09). Pitched at 800 Hz with almost nothing above the octave: a
/// version with harmonics to the fourth and harder saturation was "a very
/// high sound for a metronome" (user, 2026-09-09). The level knob is the
/// player's volume.</summary>
public static class MetronomeClick
{
    public const int SampleRate = 44100;
    /// <summary>The plain tick, every beat (one sound for every beat: user decision 2026-09-08).</summary>
    public const double TickHz = 800;
    /// <summary>The downbeat, where a caller still wants one.</summary>
    public const double AccentHz = 1200;

    public static float[] Render(double frequency, float gain) => Render(frequency, gain, SampleRate);

    /// <summary>The same tick at the rate of the stream it is mixed into (a
    /// 48 kHz file gets a 48 kHz tick), so it needs no resampling.</summary>
    public static float[] Render(double frequency, float gain, int sampleRate)
    {
        const double drive = 2.6;                                // how hard the tone leans on the saturation
        int n = (int)(sampleRate * 0.090);
        var s = new float[n];
        double norm = Math.Tanh(drive);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sampleRate;
            double env = Math.Exp(-t * 40);
            double w = 2 * Math.PI * frequency * t;
            double wave = Math.Sin(w) + 0.5 * Math.Sin(2 * w) + 0.12 * Math.Sin(3 * w);
            // A 3 ms burst of noise on the attack: the transient a speaker
            // makes audible before the tone has a period to speak.
            double burst = Math.Exp(-t * 1200) * 0.2 * Noise(i);
            double x = drive * (env * wave / 1.62 + burst);
            s[i] = (float)(gain * Math.Tanh(x) / norm);
        }
        return s;
    }

    /// <summary>Deterministic white noise in [-1, 1]: the tick sounds the same every time.</summary>
    private static double Noise(int i)
    {
        uint x = (uint)i * 2654435761u;
        x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
        return x / 2147483648.0 - 1.0;
    }
}

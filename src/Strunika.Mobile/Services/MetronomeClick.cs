namespace Strunika.Mobile.Services;

/// <summary>A short, clean metronome tick synthesized on the fly (no asset):
/// a damped tone with a hard attack, its octave and twelfth for bite, and a
/// good deal of soft saturation, 80 ms. The peak is full scale already (user:
/// "louder, several times", 2026-09-07), so more loudness comes from energy,
/// not amplitude: a slower decay, a fuller spectrum and the saturation lift
/// the RMS of the attack by ~5 dB and the energy by ~6 dB at the same peak
/// (user: "louder", 2026-09-09; measured against the 45 ms tick). Harder than
/// this and it turns into a buzzer. The level knob is the player's volume.</summary>
public static class MetronomeClick
{
    public const int SampleRate = 44100;
    /// <summary>The plain tick, every beat (one sound for every beat: user decision 2026-09-08).</summary>
    public const double TickHz = 1100;
    /// <summary>The downbeat, where a caller still wants one.</summary>
    public const double AccentHz = 1650;

    public static float[] Render(double frequency, float gain) => Render(frequency, gain, SampleRate);

    /// <summary>The same tick at the rate of the stream it is mixed into (a
    /// 48 kHz file gets a 48 kHz tick), so it needs no resampling.</summary>
    public static float[] Render(double frequency, float gain, int sampleRate)
    {
        const double drive = 3.5;                                // how hard the tone leans on the saturation
        int n = (int)(sampleRate * 0.080);
        var s = new float[n];
        double norm = Math.Tanh(drive);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sampleRate;
            double env = Math.Exp(-t * 45);
            double w = 2 * Math.PI * frequency * t;
            double wave = Math.Sin(w) + 0.6 * Math.Sin(2 * w) + 0.4 * Math.Sin(3 * w) + 0.2 * Math.Sin(4 * w);
            // A 3 ms burst of noise on the attack: the transient a speaker
            // makes audible before the tone has a period to speak.
            double burst = Math.Exp(-t * 1200) * 0.4 * Noise(i);
            double x = drive * (env * wave / 2.2 + burst);
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

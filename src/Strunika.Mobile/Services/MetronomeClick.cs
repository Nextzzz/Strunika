namespace Strunika.Mobile.Services;

/// <summary>A short, clean metronome tick synthesized on the fly (no asset):
/// a damped sine with a hard attack and a touch of its octave for bite,
/// 45 ms. Rendered to full scale — the phone's speaker and a song under it
/// need every bit (user: "louder, several times", 2026-09-07); the level
/// knob is the player's volume.</summary>
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
        int n = (int)(sampleRate * 0.045);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sampleRate;
            double env = Math.Exp(-t * 110);
            double wave = Math.Sin(2 * Math.PI * frequency * t) + 0.35 * Math.Sin(2 * Math.PI * frequency * 2 * t);
            s[i] = (float)Math.Clamp(gain * env * wave, -1, 1);
        }
        return s;
    }
}

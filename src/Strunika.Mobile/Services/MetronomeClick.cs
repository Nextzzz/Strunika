namespace Strunika.Mobile.Services;

/// <summary>A short, clean metronome tick synthesized on the fly (no asset):
/// a damped sine with a hard attack and a touch of its octave for bite,
/// 45 ms. Rendered to full scale — the phone's speaker and a song under it
/// need every bit (user: "louder, several times", 2026-09-07); the level
/// knob is the player's volume.</summary>
public static class MetronomeClick
{
    public const int SampleRate = 44100;

    public static float[] Render(double frequency, float gain)
    {
        int n = (int)(SampleRate * 0.045);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 110);
            double wave = Math.Sin(2 * Math.PI * frequency * t) + 0.35 * Math.Sin(2 * Math.PI * frequency * 2 * t);
            s[i] = (float)Math.Clamp(gain * env * wave, -1, 1);
        }
        return s;
    }
}

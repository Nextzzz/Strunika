namespace Strunika.Mobile.Services;

/// <summary>A short, clean metronome tick synthesized on the fly (no asset):
/// a damped sine with a hard attack, 18 ms.</summary>
public static class MetronomeClick
{
    public const int SampleRate = 44100;

    public static float[] Render(double frequency, float gain)
    {
        int n = (int)(SampleRate * 0.018);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * 260);
            s[i] = (float)(gain * env * Math.Sin(2 * Math.PI * frequency * t));
        }
        return s;
    }
}

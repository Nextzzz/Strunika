namespace Strunika.Core.Audio;

/// <summary>
/// A strummed guitar chord, synthesized (no samples to license or ship).
/// Karplus–Strong: a burst of noise fed through a delay line as long as the
/// string's period, smoothed a little on every pass — the same physical idea
/// behind a plucked string, and the same generator the welcome greeting uses.
/// </summary>
public static class PluckSynth
{
    public const int SampleRate = 44100;

    /// <summary>Open strings of a guitar in standard tuning, as MIDI notes
    /// (low E to high E).</summary>
    public static readonly int[] StandardTuning = { 40, 45, 50, 55, 59, 64 };

    /// <summary>
    /// One string, plucked. <paramref name="brightness"/> is how much of each
    /// pass survives (0.99 rings long and bright, 0.95 dies quickly).
    /// </summary>
    public static float[] Pluck(double frequency, double seconds, float gain = 0.5f, double brightness = 0.996, int seed = 0)
    {
        int total = Math.Max(1, (int)(seconds * SampleRate));
        int period = Math.Max(2, (int)Math.Round(SampleRate / Math.Max(20.0, frequency)));
        var line = new float[period];
        var random = new Random(seed);
        for (int i = 0; i < period; i++) line[i] = (float)(random.NextDouble() * 2 - 1);

        var output = new float[total];
        int index = 0;
        for (int i = 0; i < total; i++)
        {
            int next = (index + 1) % period;
            // Average with the neighbour: the high harmonics fade first, as on a string.
            float value = (float)((line[index] + line[next]) * 0.5 * brightness);
            output[i] = line[index] * gain;
            line[index] = value;
            index = next;
        }
        // A short fade so the note never ends on a click.
        int fade = Math.Min(total, SampleRate / 20);
        for (int i = 0; i < fade; i++) output[total - 1 - i] *= i / (float)fade;
        return output;
    }

    /// <summary>
    /// Strum a shape: <paramref name="frets"/> holds one entry per string from
    /// the low E up, −1 for a muted string and 0 for an open one, already
    /// including any capo. Strings enter one after another, low to high, the
    /// way a downstroke sounds.
    /// </summary>
    public static float[] Strum(IReadOnlyList<int> frets, double strumSeconds = 0.022, double ringSeconds = 2.2, IReadOnlyList<int>? tuning = null)
    {
        tuning ??= StandardTuning;
        int offsetStep = (int)(strumSeconds * SampleRate);
        int voices = Math.Min(frets.Count, tuning.Count);
        int total = (int)(ringSeconds * SampleRate) + offsetStep * voices + 1;
        var mix = new float[total];

        int played = 0;
        for (int s = 0; s < voices; s++)
        {
            int fret = frets[s];
            if (fret < 0) continue;                                  // muted: nothing to hear
            double frequency = 440.0 * Math.Pow(2, (tuning[s] + fret - 69) / 12.0);
            // The bass strings carry further; the top ones are thinner and shorter.
            float gain = 0.5f - 0.03f * s;
            double brightness = 0.9965 - 0.0004 * s;
            var note = Pluck(frequency, ringSeconds, gain, brightness, seed: 1000 + s);
            int start = offsetStep * played++;
            for (int i = 0; i < note.Length && start + i < total; i++) mix[start + i] += note[i];
        }
        if (played == 0) return Array.Empty<float>();

        // Normalise the sum: six strings add up well past full scale.
        float peak = 0;
        foreach (var v in mix) peak = Math.Max(peak, Math.Abs(v));
        if (peak > 0.94f)
        {
            float scale = 0.94f / peak;
            for (int i = 0; i < mix.Length; i++) mix[i] *= scale;
        }
        return mix;
    }
}

namespace Strunika.Core.Audio;

/// <summary>
/// The song's loudness envelope, compressed to one byte per frame — enough
/// to draw a waveform behind the chord track without keeping the audio.
/// </summary>
public static class Waveform
{
    /// <summary>Frames per second of the stored envelope (25 ms per frame).</summary>
    public const int Fps = 40;

    /// <summary>Bumped when the curve changes, so stored envelopes are redrawn.</summary>
    public const int Version = 2;

    /// <summary>Loudness (RMS) per frame, normalised so the loudest part reaches 255.</summary>
    public static byte[] Peaks(float[] samples, int sampleRate, int fps = Fps)
    {
        if (samples.Length == 0 || sampleRate <= 0) return Array.Empty<byte>();
        int hop = Math.Max(1, sampleRate / fps);
        int count = samples.Length / hop;
        var raw = new float[count];
        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            int start = i * hop, end = Math.Min(samples.Length, (i + 1) * hop);
            for (int s = start; s < end; s++) sum += samples[s] * (double)samples[s];
            raw[i] = end > start ? (float)Math.Sqrt(sum / (end - start)) : 0f;
        }
        // Normalise on a high percentile, not the maximum: one clipped snare
        // should not flatten the whole song.
        var sorted = (float[])raw.Clone();
        Array.Sort(sorted);
        float top = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.98)] : 1f;
        if (top < 1e-4f) top = 1e-4f;
        var bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            // A gentle curve: quiet passages stay visible, loud ones still stand
            // out (full compression is what flattened the old peak envelope).
            float v = MathF.Pow(Math.Min(1f, raw[i] / top), 0.7f);
            bytes[i] = (byte)Math.Clamp(v * 255f, 0, 255);
        }
        return bytes;
    }
}

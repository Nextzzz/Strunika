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
    public const int Version = 3;

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
        // Modern masters are compressed: the loudness of a verse and a chorus can
        // differ by a few per cent of full scale, which drawn against zero looks
        // like one flat band. So the curve is stretched between the song's own
        // quiet and loud levels — the 15th and 96th percentiles — instead of
        // between silence and the maximum. One clipped snare still cannot flatten
        // the rest, and a genuinely even song keeps a little variation because the
        // floor never rises above the median.
        var sorted = (float[])raw.Clone();
        Array.Sort(sorted);
        float At(double q) => sorted[Math.Clamp((int)(sorted.Length * q), 0, sorted.Length - 1)];
        float top = Math.Max(At(0.96), 1e-4f);
        float floor = Math.Min(At(0.15), At(0.5) * 0.9f);
        float span = Math.Max(top - floor, top * 0.15f);
        var bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            // A gentle curve above the stretch: quiet passages stay visible.
            float v = MathF.Pow(Math.Clamp((raw[i] - floor) / span, 0f, 1f), 0.75f);
            bytes[i] = (byte)Math.Clamp(v * 255f, 0, 255);
        }
        return bytes;
    }
}

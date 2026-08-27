namespace Strunika.Core.Audio;

/// <summary>
/// Windowed-sinc sample-rate conversion for decoded audio (48 k → 44.1 k
/// on iOS, where AVFoundation hands us the file's native rate). Not
/// real-time: it allocates the whole output. Quality is well above what a
/// 22.05 k chord analysis can tell apart.
/// </summary>
public static class Resampler
{
    private const int HalfTaps = 24;

    public static float[] Resample(ReadOnlySpan<float> input, int fromRate, int toRate)
    {
        if (fromRate == toRate)
            return input.ToArray();
        if (fromRate <= 0 || toRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(toRate));

        double ratio = fromRate / (double)toRate;          // input samples per output sample
        double cutoff = Math.Min(1.0, 1.0 / ratio) * 0.95; // fraction of the input Nyquist to keep
        int outLength = (int)Math.Floor(input.Length / ratio);
        var output = new float[outLength];
        // When decimating, the kernel widens with the ratio to keep the cutoff.
        double width = Math.Max(1.0, ratio);
        int half = (int)Math.Ceiling(HalfTaps * width);

        for (int n = 0; n < outLength; n++)
        {
            double centre = n * ratio;
            int i0 = (int)Math.Floor(centre);
            double acc = 0, norm = 0;
            for (int i = i0 - half + 1; i <= i0 + half; i++)
            {
                if (i < 0 || i >= input.Length) continue;
                double x = (i - centre) / width;
                double sinc = x == 0 ? 1.0 : Math.Sin(Math.PI * cutoff * x) / (Math.PI * x);
                double w = 0.5 + 0.5 * Math.Cos(Math.PI * x / HalfTaps);   // Hann over ±HalfTaps
                double k = cutoff * sinc * w;
                acc += input[i] * k;
                norm += k;
            }
            output[n] = norm > 1e-9 ? (float)(acc / norm) : 0f;
        }
        return output;
    }
}

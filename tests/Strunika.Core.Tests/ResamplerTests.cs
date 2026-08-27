using Strunika.Core.Analysis;
using Strunika.Core.Audio;

namespace Strunika.Core.Tests;

[TestFixture]
public class ResamplerTests
{
    private static float[] Sine(double frequency, int rate, double seconds)
    {
        var x = new float[(int)(rate * seconds)];
        for (int i = 0; i < x.Length; i++)
            x[i] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / rate));
        return x;
    }

    [TestCase(48000, 44100)]
    [TestCase(44100, 22050)]
    [TestCase(22050, 44100)]
    public void Resample_KeepsPitchAndLevel(int from, int to)
    {
        var input = Sine(440, from, 1.0);
        var output = Resampler.Resample(input, from, to);

        Assert.That(output.Length, Is.EqualTo(to).Within(2));
        var pitch = new PitchDetector().Detect(output.AsSpan(1000, 4096), to);
        Assert.That(pitch, Is.Not.Null);
        Assert.That(pitch!.Value.Frequency, Is.EqualTo(440).Within(1.0));

        double rms = Math.Sqrt(output.Skip(1000).Take(to - 2000).Average(v => (double)v * v));
        Assert.That(rms, Is.EqualTo(0.5 / Math.Sqrt(2)).Within(0.02));
    }

    [Test]
    public void Resample_SameRate_IsIdentity()
    {
        var input = Sine(440, 44100, 0.1);
        Assert.That(Resampler.Resample(input, 44100, 44100), Is.EqualTo(input));
    }
}

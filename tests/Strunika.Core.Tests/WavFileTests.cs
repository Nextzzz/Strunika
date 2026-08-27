using Strunika.Core.Audio;

namespace Strunika.Core.Tests;

[TestFixture]
public class WavFileTests
{
    [Test]
    public void WriteThenRead_RoundTripsWithin16BitPrecision()
    {
        var samples = TestSignals.Sine(440, 0.25);
        var path = Path.Combine(Path.GetTempPath(), $"strunika-{Guid.NewGuid():N}.wav");
        try
        {
            WavFile.Write(path, samples, TestSignals.SampleRate);
            var (read, rate) = WavFile.ReadMono(path);

            Assert.That(rate, Is.EqualTo(TestSignals.SampleRate));
            Assert.That(read.Length, Is.EqualTo(samples.Length));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(44 + samples.Length * 2));
            for (int i = 0; i < samples.Length; i += 97)
                Assert.That(read[i], Is.EqualTo(samples[i]).Within(1.0 / 32000));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Write_ClipsOutOfRangeSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"strunika-{Guid.NewGuid():N}.wav");
        try
        {
            WavFile.Write(path, new[] { 2f, -3f, 0f }, 8000);
            var (read, _) = WavFile.ReadMono(path);
            Assert.That(read[0], Is.EqualTo(32767f / 32768f).Within(1e-6));
            Assert.That(read[1], Is.EqualTo(-32767f / 32768f).Within(1e-6));
            Assert.That(read[2], Is.EqualTo(0f));
        }
        finally { File.Delete(path); }
    }
}

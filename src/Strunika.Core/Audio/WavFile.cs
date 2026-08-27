using System.Buffers.Binary;
using System.Text;

namespace Strunika.Core.Audio;

/// <summary>
/// Minimal WAV writer/reader (PCM 16-bit and IEEE float, mono or
/// interleaved). Dependency-free so recordings work the same on iOS and on
/// the Windows head; the platform decoders handle every other format.
/// </summary>
public static class WavFile
{
    /// <summary>Writes mono 16-bit PCM.</summary>
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var stream = File.Create(path);
        Write(stream, samples, sampleRate);
    }

    public static void Write(Stream stream, ReadOnlySpan<float> samples, int sampleRate)
    {
        const short channels = 1, bits = 16;
        int dataBytes = samples.Length * 2;
        Span<byte> header = stackalloc byte[44];
        Encoding.ASCII.GetBytes("RIFF", header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE", header[8..12]);
        Encoding.ASCII.GetBytes("fmt ", header[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);            // PCM
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * channels * bits / 8);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * bits / 8));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], bits);
        Encoding.ASCII.GetBytes("data", header[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);
        stream.Write(header);

        var buffer = new byte[Math.Min(dataBytes, 1 << 16)];
        int i = 0;
        while (i < samples.Length)
        {
            int n = Math.Min(buffer.Length / 2, samples.Length - i);
            for (int k = 0; k < n; k++)
            {
                float v = Math.Clamp(samples[i + k], -1f, 1f);
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(k * 2), (short)Math.Round(v * 32767));
            }
            stream.Write(buffer, 0, n * 2);
            i += n;
        }
    }

    /// <summary>Reads a PCM (8/16/24/32-bit) or IEEE-float WAV, mixing
    /// channels down to mono. The caller resamples if needed.</summary>
    public static (float[] Samples, int SampleRate) ReadMono(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException("Not a WAV file.");

        int pos = 12, format = 0, channels = 0, rate = 0, bits = 0;
        int dataStart = -1, dataLength = 0;
        while (pos + 8 <= bytes.Length)
        {
            string id = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4));
            int body = pos + 8;
            if (id == "fmt ")
            {
                format = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body));
                channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 14));
                if (format == -2 /* WAVE_FORMAT_EXTENSIBLE */ && size >= 26)
                    format = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 24));
            }
            else if (id == "data")
            {
                dataStart = body;
                dataLength = Math.Min(size, bytes.Length - body);
                break;
            }
            pos = body + size + (size & 1);
        }
        if (dataStart < 0 || channels <= 0 || rate <= 0)
            throw new InvalidDataException("WAV without fmt/data chunks.");

        int bytesPerSample = bits / 8;
        int frames = dataLength / (bytesPerSample * channels);
        var samples = new float[frames];
        var data = bytes.AsSpan(dataStart, dataLength);
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int at = (f * channels + c) * bytesPerSample;
                sum += (format, bits) switch
                {
                    (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(data[at..]),
                    (1, 8) => (data[at] - 128) / 128f,
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(data[at..]) / 32768f,
                    (1, 24) => ((data[at] | data[at + 1] << 8 | (sbyte)data[at + 2] << 16)) / 8388608f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(data[at..]) / 2147483648f,
                    _ => throw new NotSupportedException($"WAV format {format}/{bits}-bit is not supported."),
                };
            }
            samples[f] = sum / channels;
        }
        return (samples, rate);
    }
}

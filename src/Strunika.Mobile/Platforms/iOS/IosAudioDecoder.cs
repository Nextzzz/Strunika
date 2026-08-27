using AVFoundation;
using Foundation;
using Strunika.Core.Audio;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// AVFoundation decoder: <see cref="AVAudioFile"/> yields deinterleaved
/// float32 at the file's native rate; channels are mixed to mono here and
/// the rate converted with the dependency-free <see cref="Resampler"/>
/// (no AVAudioConverter callback plumbing). Written on the Windows head
/// against the AVFoundation binding docs — verify on the first device
/// build (README "iOS TODO").
/// </summary>
public sealed class IosAudioDecoder : IAudioDecoder
{
    public Task<float[]> DecodeMonoAsync(string path, int sampleRate, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var (wav, rate) = WavFile.ReadMono(path);
                return Resampler.Resample(wav, rate, sampleRate);
            }

            using var file = new AVAudioFile(NSUrl.FromFilename(path), out NSError? openError);
            if (openError != null)
                throw new IOException(openError.LocalizedDescription);

            var format = file.ProcessingFormat;   // float32, non-interleaved
            int channels = (int)format.ChannelCount;
            int nativeRate = (int)format.SampleRate;
            long frames = file.Length;
            var mono = new float[frames];
            const uint chunk = 1 << 16;
            using var buffer = new AVAudioPcmBuffer(format, chunk);
            long written = 0;
            while (written < frames)
            {
                ct.ThrowIfCancellationRequested();
                if (!file.ReadIntoBuffer(buffer, out NSError? readError) || readError != null)
                    throw new IOException(readError?.LocalizedDescription ?? "read failed");
                int got = (int)buffer.FrameLength;
                if (got == 0) break;
                unsafe
                {
                    float** data = (float**)buffer.FloatChannelData;
                    for (int i = 0; i < got; i++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++) sum += data[c][i];
                        mono[written + i] = sum / channels;
                    }
                }
                written += got;
            }
            if (written < frames)
                Array.Resize(ref mono, (int)written);
            return Resampler.Resample(mono, nativeRate, sampleRate);
        }, ct);
}

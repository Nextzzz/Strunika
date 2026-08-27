namespace Strunika.Mobile.Services;

/// <summary>
/// File → mono float samples at the requested rate. iOS decodes through
/// AVFoundation (mp3/m4a/wav/aac…), the Windows head through NAudio /
/// MediaFoundation. WAV recordings work everywhere via <c>WavFile</c>.
/// </summary>
public interface IAudioDecoder
{
    Task<float[]> DecodeMonoAsync(string path, int sampleRate, CancellationToken ct = default);
}

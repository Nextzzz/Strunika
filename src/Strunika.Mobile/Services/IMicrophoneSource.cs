namespace Strunika.Mobile.Services;

/// <summary>
/// Platform microphone as a stream of mono float chunks at 44100 Hz —
/// the same contract the analysis pipeline consumes on desktop.
/// </summary>
public interface IMicrophoneSource : IDisposable
{
    const int SampleRate = 44100;

    event Action<float[]>? ChunkAvailable;

    bool IsRunning { get; }

    /// <summary>Starts capture; false when the microphone permission
    /// was denied by the user.</summary>
    Task<bool> StartAsync();

    void Stop();
}

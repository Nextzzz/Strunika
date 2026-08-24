using Strunika.Media;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>Thin adapter over the desktop capture so UI and view-models
/// iterate on the PC without a phone attached.</summary>
public sealed class WindowsMicrophoneSource : IMicrophoneSource
{
    private readonly MicrophoneCapture _capture = new();

    public event Action<float[]>? ChunkAvailable;

    public bool IsRunning => _capture.IsRunning;

    public WindowsMicrophoneSource()
    {
        _capture.ChunkAvailable += chunk => ChunkAvailable?.Invoke(chunk);
    }

    public Task<bool> StartAsync()
    {
        _capture.Start();
        return Task.FromResult(true);
    }

    public void Stop() => _capture.Stop();

    public void Dispose() => _capture.Dispose();
}

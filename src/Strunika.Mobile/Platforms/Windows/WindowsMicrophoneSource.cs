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

    /// <summary>False when there is no input to open, or it is taken: the tuner
    /// starts by itself as soon as it is shown, so this must never throw.</summary>
    public Task<bool> StartAsync()
    {
        try
        {
            _capture.Start();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error("microphone: start", ex);
            try { _capture.Stop(); } catch (Exception) { }
            return Task.FromResult(false);
        }
    }

    public void Stop() => _capture.Stop();

    public void Dispose() => _capture.Dispose();
}

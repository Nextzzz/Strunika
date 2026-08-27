using NAudio.Wave;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>NAudio one-shot playback on the dev head.</summary>
public sealed class WindowsSoundPlayer : ISoundPlayer
{
    public async Task PlayAsync(string asset)
    {
        var path = await SoundAssets.EnsureAsync(asset);
        if (path == null) return;
        // Opening the output device takes a noticeable moment: never on the UI thread.
        await Task.Run(() =>
        {
            try
            {
                var reader = new AudioFileReader(path);
                var output = new WaveOutEvent();
                output.Init(reader);
                output.PlaybackStopped += (_, _) => { output.Dispose(); reader.Dispose(); };
                output.Play();
            }
            catch (Exception ex)
            {
                Strunika.Core.Diagnostics.FileLog.Error("sound play", ex);
            }
        });
    }
}

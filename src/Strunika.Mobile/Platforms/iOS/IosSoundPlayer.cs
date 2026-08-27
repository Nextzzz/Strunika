using AVFoundation;
using Foundation;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// AVAudioPlayer one-shot playback. The session is set to Ambient so the
/// strum respects the ring/silent switch and mixes with whatever the user
/// is already listening to. Unverified until the first device build.
/// </summary>
public sealed class IosSoundPlayer : ISoundPlayer
{
    private AVAudioPlayer? _player;   // kept alive until playback ends

    public async Task PlayAsync(string asset, double volume = 1.0)
    {
        var path = await SoundAssets.EnsureAsync(asset);
        if (path == null) return;
        await Task.Yield();   // let the caller's animation start first
        try
        {
            AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Ambient, AVAudioSessionCategoryOptions.MixWithOthers);
            AVAudioSession.SharedInstance().SetActive(true);
            _player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(path), out NSError? error);
            if (error != null || _player == null)
            {
                Strunika.Core.Diagnostics.FileLog.Error("sound play: " + error?.LocalizedDescription);
                return;
            }
            _player.Volume = (float)Math.Clamp(volume, 0, 1);
            _player.FinishedPlaying += (_, _) => { _player?.Dispose(); _player = null; };
            _player.PrepareToPlay();
            _player.Play();
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error("sound play", ex);
        }
    }
}

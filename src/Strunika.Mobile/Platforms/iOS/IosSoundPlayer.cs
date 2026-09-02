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
    private AVAudioPlayer? _player;   // the last one played; the next play releases it, never its own callback

    public async Task PlayAsync(string asset, double volume = 1.0)
    {
        var path = await SoundAssets.EnsureAsync(asset);
        if (path == null) return;
        await Task.Yield();   // let the caller's animation start first
        try
        {
            AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Ambient, AVAudioSessionCategoryOptions.MixWithOthers);
            AVAudioSession.SharedInstance().SetActive(true);
            // The previous player is released here, before the next one starts,
            // and nowhere else. Disposing it inside its own FinishedPlaying aborts
            // the process ("the player object was Dispose()d during the callback"),
            // and MainThread.BeginInvokeOnMainThread runs its action synchronously
            // when already on the main thread — which the callback is — so
            // deferring through it changed nothing (build 13 crashed the same way).
            if (_player is { } previous)
            {
                _player = null;
                try { previous.Stop(); previous.Dispose(); } catch { /* already gone */ }
            }
            var player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(path), out NSError? error);
            if (error != null || player == null)
            {
                Strunika.Core.Diagnostics.FileLog.Error("sound play: " + error?.LocalizedDescription);
                return;
            }
            player.Volume = (float)Math.Clamp(volume, 0, 1);
            _player = player;
            player.PrepareToPlay();
            player.Play();
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error("sound play", ex);
        }
    }
}

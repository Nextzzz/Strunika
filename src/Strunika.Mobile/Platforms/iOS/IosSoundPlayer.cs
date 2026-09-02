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
    private AVAudioPlayer? _player;   // kept alive until playback ends, released after its callback

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
            // Never dispose the player inside its own FinishedPlaying: the runtime
            // treats a delegate whose player vanished mid-callback as a corrupted
            // state and aborts the process — this took the app down five seconds
            // after launch, as the greeting ended. Release it on the next turn of
            // the main loop instead, once the callback has returned.
            var player = _player;
            player.FinishedPlaying += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ReferenceEquals(_player, player)) _player = null;
                player.Dispose();
            });
            player.PrepareToPlay();
            player.Play();
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error("sound play", ex);
        }
    }
}

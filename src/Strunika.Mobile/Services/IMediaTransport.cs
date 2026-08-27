using Strunika.Mobile.Controls;

namespace Strunika.Mobile.Services;

/// <summary>What the song page learns from a transport ~5×/s.</summary>
/// <param name="Playing">The audio is really advancing right now.</param>
/// <param name="Pending">We asked for play and the player has not started yet
/// (page loading, buffering, cued) — the button shows "pause", the conveyor waits.</param>
public readonly record struct TransportState(double Position, double Duration, bool Playing, bool Pending);

/// <summary>What the song page drives, regardless of where the audio comes
/// from: a local file (<see cref="IAudioPlayer"/>) or the YouTube embed.</summary>
public interface IMediaTransport : IDisposable
{
    Task PlayAsync();
    Task PauseAsync();
    Task SeekAsync(double seconds);
    Task SetRateAsync(double rate);
    /// <summary>0–1.</summary>
    Task SetVolumeAsync(double volume);
    Task<TransportState> PollAsync();
    bool SupportsRate { get; }
    /// <summary>False while a YouTube page is still loading its player.</summary>
    bool IsReady { get; }
}

public sealed class FileTransport : IMediaTransport
{
    private readonly IAudioPlayer _player;
    public FileTransport(IAudioPlayer player) => _player = player;
    public Task PlayAsync() { _player.Play(); return Task.CompletedTask; }
    public Task PauseAsync() { _player.Pause(); return Task.CompletedTask; }
    public Task SeekAsync(double seconds) { _player.Position = seconds; return Task.CompletedTask; }
    public Task SetRateAsync(double rate) { _player.Rate = rate; return Task.CompletedTask; }
    public Task SetVolumeAsync(double volume) { _player.Volume = volume; return Task.CompletedTask; }
    public Task<TransportState> PollAsync() => Task.FromResult(new TransportState(_player.Position, _player.Duration, _player.IsPlaying, false));
    public bool SupportsRate => DeviceInfo.Platform == DevicePlatform.iOS;
    public bool IsReady => true;
    public void Dispose() => _player.Dispose();
}

public sealed class YouTubeTransport : IMediaTransport
{
    private readonly YouTubeEmbedView _view;
    private double _fallbackDuration, _lastPosition;
    /// <summary>What we asked for, mirrored from the page once it answers.</summary>
    private bool _intent;
    public bool IsReady { get; private set; }

    public YouTubeTransport(YouTubeEmbedView view, double knownDuration) { _view = view; _fallbackDuration = knownDuration; }
    public Task PlayAsync() { _intent = true; return _view.PlayAsync(); }
    public Task PauseAsync() { _intent = false; return _view.PauseAsync(); }
    public Task SeekAsync(double seconds) { _lastPosition = seconds; return _view.SeekAsync(seconds); }
    public Task SetRateAsync(double rate) => _view.SetRateAsync(rate);
    public Task SetVolumeAsync(double volume) => _view.SetVolumeAsync(volume);

    public async Task<TransportState> PollAsync()
    {
        var s = await _view.ProbeAsync();
        IsReady = s != null;
        // The page is not ready yet (or the call failed): report what we asked
        // for, never "stopped at zero".
        if (s == null) return new TransportState(_lastPosition, _fallbackDuration, false, _intent);
        if (s.Value.Duration > 0) _fallbackDuration = s.Value.Duration;
        _intent = s.Value.WantPlay;
        // Unstarted (-1) and cued (5) report position 0 although the video simply
        // has not begun; buffering (3) reports a position that is not moving.
        // None of those is "playing" for the conveyor — only state 1 is.
        bool playing = s.Value.PlayerState == 1;
        if (s.Value.PlayerState is -1 or 5)
        {
            if (_intent) await _view.PlayAsync();
            return new TransportState(_lastPosition, _fallbackDuration, false, _intent);
        }
        _lastPosition = s.Value.Position;
        return new TransportState(s.Value.Position, _fallbackDuration, playing, _intent && !playing);
    }

    public bool SupportsRate => true;
    public void Dispose() { }
}

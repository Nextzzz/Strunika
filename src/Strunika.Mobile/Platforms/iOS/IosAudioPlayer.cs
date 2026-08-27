using AVFoundation;
using Foundation;
using Strunika.Core.Audio;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>AVAudioPlayer with pitch-preserving rate (EnableRate). Unverified until a device build.</summary>
public sealed class IosAudioPlayer : IAudioPlayer
{
    private AVAudioPlayer? _player;
    private double _rate = 1.0;

    public Task LoadAsync(string path)
    {
        Dispose();
        AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback);
        AVAudioSession.SharedInstance().SetActive(true);
        _player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(path), out NSError? error);
        if (error != null || _player == null)
            throw new IOException(error?.LocalizedDescription ?? "cannot open audio");
        _player.EnableRate = true;
        _player.Rate = (float)_rate;
        _player.PrepareToPlay();
        return Task.CompletedTask;
    }

    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public bool IsPlaying => _player?.Playing ?? false;
    public double Duration => _player?.Duration ?? 0;

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_player != null) _player.Volume = (float)_volume;
        }
    }

    public double Position
    {
        get => _player?.CurrentTime ?? 0;
        set { if (_player != null) _player.CurrentTime = Math.Clamp(value, 0, Math.Max(0, Duration)); }
    }

    public double Rate
    {
        get => _rate;
        set { _rate = Math.Clamp(value, 0.5, 1.25); if (_player != null) _player.Rate = (float)_rate; }
    }

    public void Dispose()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }
}

/// <summary>A small pool of prepared AVAudioPlayers over an in-memory WAV tick.</summary>
public sealed class IosClickPlayer : IClickPlayer
{
    public double Volume { get; set; } = 1.0;

    private readonly AVAudioPlayer[] _ticks, _accents;
    private int _next;

    public IosClickPlayer()
    {
        _ticks = Pool(MetronomeClick.Render(1100, 0.5f));
        _accents = Pool(MetronomeClick.Render(1650, 0.7f));
    }

    private static AVAudioPlayer[] Pool(float[] samples)
    {
        using var ms = new MemoryStream();
        WavFile.Write(ms, samples, MetronomeClick.SampleRate);
        var data = NSData.FromArray(ms.ToArray());
        var pool = new AVAudioPlayer[3];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = AVAudioPlayer.FromData(data, out _)!;
            pool[i].PrepareToPlay();
        }
        return pool;
    }

    public void Click(bool accent)
    {
        var pool = accent ? _accents : _ticks;
        var p = pool[_next++ % pool.Length];
        p.Volume = (float)Math.Clamp(Volume, 0, 1);
        p.CurrentTime = 0;
        p.Play();
    }

    public void Dispose()
    {
        foreach (var p in _ticks.Concat(_accents)) p.Dispose();
    }
}

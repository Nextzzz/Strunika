namespace Strunika.Mobile.Services;

/// <summary>
/// Writes metronome ticks into a song's own sample stream, each at its beat's
/// sample. A file player hands it every block it is about to play, with the
/// block's first frame; the ticks that fall inside (and the tail of one that
/// began in the block before) are added on top of the song. Shared by the
/// iOS engine player and the Windows one — the arithmetic is the same, only
/// the sample layout differs.
/// </summary>
public sealed class TickMixer
{
    private readonly int _rate;
    private readonly float[] _tick;
    private readonly object _gate = new();
    private long[] _beatFrames = Array.Empty<long>();
    private bool _on;
    private float _volume = 1f;

    public TickMixer(int sampleRate)
    {
        _rate = sampleRate;
        _tick = MetronomeClick.Render(MetronomeClick.TickHz, 1f, sampleRate);
    }

    public void Set(double[] beats, bool on, double volume)
    {
        var frames = new long[beats.Length];
        for (int i = 0; i < beats.Length; i++) frames[i] = (long)Math.Round(beats[i] * _rate);
        lock (_gate)
        {
            _beatFrames = frames;
            _on = on;
            _volume = (float)Math.Clamp(volume, 0, 1);
        }
    }

    /// <summary>Adds the ticks of frames [<paramref name="firstFrame"/>,
    /// firstFrame + channel.Length) to one channel of planar samples.</summary>
    public void MixChannel(long firstFrame, Span<float> channel) => Mix(firstFrame, channel, 1);

    /// <summary>The same for interleaved samples: <paramref name="data"/> holds
    /// data.Length / channels frames; the tick goes to every channel.</summary>
    public void MixInterleaved(long firstFrame, Span<float> data, int channels) => Mix(firstFrame, data, channels);

    private void Mix(long firstFrame, Span<float> data, int channels)
    {
        long[] beats; float volume;
        lock (_gate)
        {
            if (!_on || _volume <= 0) return;
            beats = _beatFrames;
            volume = _volume;
        }
        int frames = data.Length / channels;
        long lastFrame = firstFrame + frames;
        // The first beat whose tick can still be sounding at firstFrame.
        int i = Array.BinarySearch(beats, firstFrame - _tick.Length);
        if (i < 0) i = ~i;
        for (; i < beats.Length && beats[i] < lastFrame; i++)
        {
            long beat = beats[i];
            long from = Math.Max(beat, firstFrame), to = Math.Min(beat + _tick.Length, lastFrame);
            for (long f = from; f < to; f++)
            {
                float v = _tick[f - beat] * volume;
                int at = (int)(f - firstFrame) * channels;
                for (int c = 0; c < channels; c++) data[at + c] = Math.Clamp(data[at + c] + v, -1f, 1f);
            }
        }
    }
}

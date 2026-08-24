using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Analysis;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

/// <summary>
/// Tuner with a Guitar-Tuna feel: YIN on a rolling window, smoothed
/// needle (EMA), a ±8-cent green zone, and a hold — when the string
/// decays and detection drops out, the last stable reading stays on
/// screen for a moment instead of flashing away.
/// </summary>
public partial class TunerViewModel : ObservableObject
{
    private const int WindowSize = 4096;
    private const double InTuneCents = 8.0;
    private const double NeedlePixelsPerCent = 3.0;   // track spans ±50 cents = ±150 px
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(1200);

    private readonly IMicrophoneSource _microphone;
    private readonly PitchDetector _detector = new();
    private readonly float[] _buffer = new float[WindowSize * 2];
    private readonly object _lock = new();
    private int _filled;
    private IDispatcherTimer? _timer;
    private double _smoothedCents;
    private string _lastNote = "";
    private DateTime _lastGoodAt = DateTime.MinValue;

    [ObservableProperty]
    private string noteName = "—";

    [ObservableProperty]
    private string hint = "Натисни «Слухати» і зіграй ноту";

    /// <summary>Needle offset in device units (bound to TranslationX).</summary>
    [ObservableProperty]
    private double needleOffset;

    [ObservableProperty]
    private bool inTune;

    [ObservableProperty]
    private bool listening;

    public TunerViewModel(IMicrophoneSource microphone)
    {
        _microphone = microphone;
        _microphone.ChunkAvailable += OnChunk;
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Listening)
        {
            _microphone.Stop();
            _timer?.Stop();
            Listening = false;
            Reset();
            Hint = "Натисни «Слухати» і зіграй ноту";
            return;
        }

        if (!await _microphone.StartAsync())
        {
            Hint = "Нема доступу до мікрофона — дозволь у Налаштуваннях.";
            return;
        }
        Listening = true;
        Hint = "";
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(80);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Reset()
    {
        NoteName = "—";
        NeedleOffset = 0;
        InTune = false;
        _lastNote = "";
        _smoothedCents = 0;
    }

    private void OnChunk(float[] chunk)
    {
        lock (_lock)
        {
            if (chunk.Length >= _buffer.Length)
            {
                Array.Copy(chunk, chunk.Length - _buffer.Length, _buffer, 0, _buffer.Length);
            }
            else
            {
                int keep = _buffer.Length - chunk.Length;
                Array.Copy(_buffer, chunk.Length, _buffer, 0, keep);
                Array.Copy(chunk, 0, _buffer, keep, chunk.Length);
            }
            _filled = Math.Min(_buffer.Length, _filled + chunk.Length);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        float[] window;
        lock (_lock)
        {
            if (_filled < WindowSize)
                return;
            window = _buffer[^WindowSize..];
        }

        var pitch = _detector.Detect(window, IMicrophoneSource.SampleRate);
        if (pitch == null)
        {
            // Hold the last reading through the decay; then clear.
            if (_lastNote != "" && DateTime.Now - _lastGoodAt > Hold)
                Reset();
            return;
        }

        var (name, octave, cents) = Notes.Describe(pitch.Value.Frequency);
        string note = $"{name}{octave}";
        // A new note starts the needle fresh; the same note is smoothed.
        _smoothedCents = note == _lastNote ? 0.65 * _smoothedCents + 0.35 * cents : cents;
        _lastNote = note;
        _lastGoodAt = DateTime.Now;

        NoteName = note;
        NeedleOffset = Math.Clamp(_smoothedCents, -50, 50) * NeedlePixelsPerCent;
        InTune = Math.Abs(_smoothedCents) <= InTuneCents;
    }
}

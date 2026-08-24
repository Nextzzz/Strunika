using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Analysis;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

/// <summary>Same tuner logic as desktop: rolling 4096-sample window,
/// YIN every 100 ms, note + cents.</summary>
public partial class TunerViewModel : ObservableObject
{
    private const int WindowSize = 4096;

    private readonly IMicrophoneSource _microphone;
    private readonly PitchDetector _detector = new();
    private readonly float[] _buffer = new float[WindowSize * 2];
    private readonly object _lock = new();
    private int _filled;
    private IDispatcherTimer? _timer;

    [ObservableProperty]
    private string noteName = "—";

    [ObservableProperty]
    private string details = "Натисни «Слухати» і зіграй ноту";

    /// <summary>Deviation in cents (-50..50) for the needle.</summary>
    [ObservableProperty]
    private double cents;

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
            NoteName = "—";
            Details = "Натисни «Слухати» і зіграй ноту";
            return;
        }

        if (!await _microphone.StartAsync())
        {
            Details = "Нема доступу до мікрофона — дозволь у Налаштуваннях.";
            return;
        }
        Listening = true;
        Details = "Слухаю…";
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
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
            return;

        var (name, octave, cents) = Notes.Describe(pitch.Value.Frequency);
        NoteName = $"{name}{octave}";
        Cents = Math.Clamp(cents, -50, 50);
        InTune = Math.Abs(cents) <= 5;
        Details = $"{pitch.Value.Frequency:F1} Гц   {cents:+0.0;-0.0} центів";
    }
}

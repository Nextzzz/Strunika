using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Analysis;
using Strunika.Core.Realtime;
using Strunika.Mobile.Services;
using Strunika.Neural;

namespace Strunika.Mobile.ViewModels;

/// <summary>
/// Mobile twin of the desktop live tab: DSP provisional guess (grey)
/// + neural confirmation (big), simple-chords display, history with
/// same-root revision. Model files are unpacked from app assets on
/// first start.
/// </summary>
public partial class LiveViewModel : ObservableObject, IDisposable
{
    private readonly IMicrophoneSource _microphone;
    private readonly StreamingChordDetector _dsp;
    private SlidingNeuralChordDetector? _neural;
    private IDispatcherTimer? _tickTimer;

    [ObservableProperty]
    private string provisionalChord = "";

    [ObservableProperty]
    private string confirmedChord = "—";

    [ObservableProperty]
    private string status = "Натисни «Слухати» і грай";

    [ObservableProperty]
    private bool listening;

    [ObservableProperty]
    private bool simpleChords = true;

    [ObservableProperty]
    private bool reviseHistory = true;

    /// <summary>Which bundled model confirms chords; switchable live.</summary>
    public ObservableCollection<string> LiveModels { get; } = new() { "Гітарна", "Базова" };

    [ObservableProperty]
    private string selectedLiveModel = "Гітарна";

    private static readonly Dictionary<string, string> ModelFiles = new()
    {
        ["Гітарна"] = "btc_guitar2",
        ["Базова"] = "btc_large_voca",
    };

    partial void OnSelectedLiveModelChanged(string value)
    {
        if (Listening || _neural != null)
            _ = SwitchModelAsync(value);
    }

    private async Task SwitchModelAsync(string name)
    {
        if (!ModelFiles.TryGetValue(name, out var file))
            return;
        var old = _neural;
        _neural = null;
        if (old != null)
        {
            old.ConfirmedChanged -= OnConfirmed;
            old.Dispose();
        }
        Status = "Розпаковую модель…";
        var next = await ModelStore.CreateDetectorAsync(file);
        if (next != null)
            next.ConfirmedChanged += OnConfirmed;
        _neural = next;
        ConfirmedChord = "—";
        Status = next == null ? "Модель не знайдена" : (Listening ? "Слухаю…" : "Готово");
    }

    public ObservableCollection<string> History { get; } = new();

    public LiveViewModel(IMicrophoneSource microphone)
    {
        _microphone = microphone;
        _dsp = new StreamingChordDetector(IMicrophoneSource.SampleRate);
        _dsp.ChordChanged += OnProvisional;
        _microphone.ChunkAvailable += chunk =>
        {
            _dsp.AddSamples(chunk);
            _neural?.AddSamples(chunk);
        };
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Listening)
        {
            _microphone.Stop();
            _tickTimer?.Stop();
            Listening = false;
            Status = "Зупинено";
            return;
        }

        if (_neural == null)
            await SwitchModelAsync(SelectedLiveModel);

        if (!await _microphone.StartAsync())
        {
            Status = "Нема доступу до мікрофона — дозволь у Налаштуваннях.";
            return;
        }

        Listening = true;
        Status = _neural == null ? "Слухаю (без нейромережі)" : "Слухаю…";
        _tickTimer ??= Application.Current!.Dispatcher.CreateTimer();
        _tickTimer.Interval = TimeSpan.FromMilliseconds(250);
        _tickTimer.Tick -= OnTick;
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var neural = _neural;
        if (Listening && neural != null)
            try { neural.Tick(); }
            catch (ObjectDisposedException) { }
    }

    private void OnProvisional(Chord chord)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            string label = chord.Label == "N" ? "—" : chord.Label;
            if (_neural == null)
            {
                ConfirmedChord = label;
                if (chord != Chord.None)
                    PushHistory(chord.Label);
            }
            else
            {
                ProvisionalChord = label;
            }
        });
    }

    private void OnConfirmed(string rawLabel)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            string pretty = ChordLabels.Pretty(rawLabel);
            if (SimpleChords)
                pretty = ChordLabels.Simplify(pretty);
            ConfirmedChord = pretty;
            if (pretty != "—")
                PushHistory(pretty);
        });
    }

    private string? _lastHistoryLabel;
    private DateTime _lastHistoryAt;

    /// <summary>Same-root refinements within 2.5 s rewrite the last
    /// entry in place (Chord AI-style); a different root always appends.</summary>
    private void PushHistory(string label)
    {
        if (label == _lastHistoryLabel)
            return;
        bool sameRoot = ReviseHistory && History.Count > 0
            && _lastHistoryLabel is { } prev && prev != "—" && label != "—"
            && RootOf(label) == RootOf(prev);
        bool revision = sameRoot && (DateTime.Now - _lastHistoryAt).TotalSeconds < 2.5;
        _lastHistoryLabel = label;
        if (revision)
        {
            History[0] = $"{_lastHistoryAt:HH:mm:ss}   {label}";
            return;
        }
        _lastHistoryAt = DateTime.Now;
        History.Insert(0, $"{_lastHistoryAt:HH:mm:ss}   {label}");
        while (History.Count > 50)
            History.RemoveAt(History.Count - 1);
    }

    private static string RootOf(string pretty) =>
        pretty.Length > 1 && pretty[1] == '#' ? pretty[..2] : pretty[..1];

    public void Dispose()
    {
        _tickTimer?.Stop();
        _neural?.Dispose();
    }
}

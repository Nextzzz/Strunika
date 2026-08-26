using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Analysis;
using Strunika.Core.Realtime;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Neural;

namespace Strunika.Mobile.ViewModels;

public sealed record ModelOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Mobile twin of the desktop live tab: DSP provisional guess (grey)
/// + neural confirmation (big), simple-chords display, history with
/// same-root revision. Model files are unpacked from app assets on
/// first start.
/// </summary>
public partial class LiveViewModel : ObservableObject, IDisposable
{
    private readonly IMicrophoneSource _microphone;
    private readonly IProGate _pro;
    private readonly StreamingChordDetector _dsp;
    private SlidingNeuralChordDetector? _neural;
    private IDispatcherTimer? _tickTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuessText))]
    private string provisionalChord = "";

    /// <summary>"здогадка · A" above the hero, empty while nothing is heard.</summary>
    public string GuessText => string.IsNullOrEmpty(ProvisionalChord) || ProvisionalChord == "—"
        ? "" : $"{Loc.Get("Live_Guess")} · {ProvisionalChord}";

    [ObservableProperty]
    private string confirmedChord = "";

    [ObservableProperty]
    private string status = Loc.Get("Live_Idle");

    [ObservableProperty]
    private bool listening;

    [ObservableProperty]
    private bool simpleChords = true;

    [ObservableProperty]
    private bool reviseHistory = true;

    /// <summary>Turning simple chords OFF is a Pro feature.</summary>
    public bool FullVocabularyLocked => !_pro.Has(Feature.FullChordVocabulary);

    /// <summary>Model picker is shown only when "Expert settings" is on.</summary>
    [ObservableProperty]
    private bool expertMode = AppSettings.Expert;

    /// <summary>Raised when the user touches a locked feature; the view opens the paywall.</summary>
    public event EventHandler<Feature>? ProRequired;

    /// <summary>Which bundled model confirms chords; switchable live.
    /// DEFAULT = base for live play (product decision, Aug 2026):
    /// the base generalist is the steadiest for real-time strumming.
    /// Guitar2 stays selectable for solo/mic experiments.</summary>
    public ObservableCollection<ModelOption> LiveModels { get; } = new()
    {
        new("btc_large_voca", Loc.Get("Model_Base")),
        new("btc_guitar2", Loc.Get("Model_Guitar")),
    };

    [ObservableProperty]
    private ModelOption? selectedLiveModel;

    public ObservableCollection<string> History { get; } = new();

    public LiveViewModel(IMicrophoneSource microphone, IProGate pro)
    {
        _microphone = microphone;
        _pro = pro;
        _pro.Changed += (_, _) => OnPropertyChanged(nameof(FullVocabularyLocked));
        selectedLiveModel = LiveModels[0];
        _dsp = new StreamingChordDetector(IMicrophoneSource.SampleRate);
        _dsp.ChordChanged += OnProvisional;
        _microphone.ChunkAvailable += chunk =>
        {
            _dsp.AddSamples(chunk);
            _neural?.AddSamples(chunk);
        };
        AppSettings.Changed += (_, key) =>
        {
            if (key == nameof(AppSettings.Expert))
                ExpertMode = AppSettings.Expert;
        };
        Loc.Instance.PropertyChanged += (_, _) =>
        {
            LiveModels[0] = LiveModels[0] with { Name = Loc.Get("Model_Base") };
            LiveModels[1] = LiveModels[1] with { Name = Loc.Get("Model_Guitar") };
            if (!Listening) Status = Loc.Get("Live_Idle");
        };
    }

    partial void OnSimpleChordsChanged(bool value)
    {
        if (!value && FullVocabularyLocked)
        {
            SimpleChords = true;
            ProRequired?.Invoke(this, Feature.FullChordVocabulary);
        }
    }

    partial void OnSelectedLiveModelChanged(ModelOption? value)
    {
        if (value != null && (Listening || _neural != null))
            _ = SwitchModelAsync(value.Id);
    }

    private async Task SwitchModelAsync(string file)
    {
        var old = _neural;
        _neural = null;
        if (old != null)
        {
            old.ConfirmedChanged -= OnConfirmed;
            old.Dispose();
        }
        Status = Loc.Get("Live_Unpacking");
        var next = await ModelStore.CreateDetectorAsync(file);
        if (next != null)
            next.ConfirmedChanged += OnConfirmed;
        _neural = next;
        ConfirmedChord = "";
        Status = next == null ? Loc.Get("Live_ModelMissing")
            : Listening ? Loc.Get("Live_Listening") : Loc.Get("Live_Ready");
    }

    /// <summary>Stops the microphone; also called when the user leaves the tab.</summary>
    public void StopListening()
    {
        if (!Listening) return;
        _microphone.Stop();
        _tickTimer?.Stop();
        Listening = false;
        Status = Loc.Get("Live_Stopped");
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Listening)
        {
            StopListening();
            return;
        }

        if (_neural == null && SelectedLiveModel != null)
            await SwitchModelAsync(SelectedLiveModel.Id);

        if (!await _microphone.StartAsync())
        {
            Status = Loc.Get("Tuner_NoMic");
            return;
        }

        Listening = true;
        Status = _neural == null ? Loc.Get("Live_NoNeural") : Loc.Get("Live_Listening");
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
    /// entry in place (Chord AI-style); a different root always appends.
    /// Newest entry is last, so the ribbon reads left → right in time.</summary>
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
            History[^1] = label;
            return;
        }
        _lastHistoryAt = DateTime.Now;
        History.Add(label);
        while (History.Count > 50)
            History.RemoveAt(0);
    }

    private static string RootOf(string pretty) =>
        pretty.Length > 1 && pretty[1] == '#' ? pretty[..2] : pretty[..1];

    public void Dispose()
    {
        _tickTimer?.Stop();
        _neural?.Dispose();
    }
}

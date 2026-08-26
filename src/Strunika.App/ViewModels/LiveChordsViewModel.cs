using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Strunika.Core.Analysis;
using Strunika.Core.Realtime;
using Strunika.Media;
using Strunika.Neural;

namespace Strunika.App.ViewModels;

/// <summary>
/// Two-tier live display: the DSP engine gives an instant provisional
/// guess (~0.3 s, grey), the neural sliding window confirms ~1 s later
/// (big and bright). Without a model file the DSP tier drives the big
/// display alone.
/// </summary>
public partial class LiveChordsViewModel : ObservableObject, IDisposable
{
    private readonly MicrophoneCapture _capture;
    private readonly StreamingChordDetector _dsp;
    private SlidingNeuralChordDetector? _neural;
    private readonly System.Timers.Timer? _tickTimer;
    private readonly Dictionary<string, string> _modelPaths = new();

    [ObservableProperty]
    private string provisionalChord = "";

    [ObservableProperty]
    private string confirmedChord = "—";

    [ObservableProperty]
    private bool neuralAvailable;

    /// <summary>Noise-gate sensitivity in dB; bound to the UI slider.</summary>
    [ObservableProperty]
    private double gateMarginDb = 12.0;

    partial void OnGateMarginDbChanged(double value) => _dsp.GateMarginDb = value;

    /// <summary>Show triads instead of extensions (E7 → E). Default ON:
    /// live playing is about the chord family, and the model likes to
    /// "hear" sevenths that pop context suggests but the player never
    /// strummed. Display-only.</summary>
    [ObservableProperty]
    private bool simpleChords = true;

    /// <summary>Refine-in-place for the history (same-root corrections
    /// rewrite the last entry). Off = every confirmation appends.</summary>
    [ObservableProperty]
    private bool reviseHistory = true;

    /// <summary>Model choice for the confirming tier; switchable live.</summary>
    public ObservableCollection<string> LiveModels { get; } = new();

    [ObservableProperty]
    private string selectedLiveModel = "";

    partial void OnSelectedLiveModelChanged(string value) => SwitchModel(value);

    public ObservableCollection<string> History { get; } = new();

    public LiveChordsViewModel(MicrophoneCapture capture, string? guitarModelPath,
                               string? baseModelPath = null, string? selfModelPath = null,
                               string? fullModelPath = null)
    {
        _capture = capture;
        _dsp = new StreamingChordDetector(MicrophoneCapture.SampleRate);
        _dsp.ChordChanged += OnProvisional;

        foreach (var (name, path) in new[]
                 { ("Гітарна", guitarModelPath), ("Базова", baseModelPath),
                   ("Self", selfModelPath), ("Full", fullModelPath) })
            if (path != null && File.Exists(path) && !_modelPaths.ContainsValue(path))
            {
                _modelPaths[name] = path;
                LiveModels.Add(name);
            }

        Strunika.Core.Diagnostics.FileLog.Info(
            $"Live models: [{string.Join(", ", LiveModels)}]");
        if (_modelPaths.Count > 0)
        {
            NeuralAvailable = true;
            SelectedLiveModel = LiveModels[0]; // triggers SwitchModel

            _tickTimer = new System.Timers.Timer(250);
            _tickTimer.Elapsed += (_, _) =>
            {
                var neural = _neural;
                if (_capture.IsRunning && neural != null)
                    try { neural.Tick(); }
                    catch (ObjectDisposedException) { /* mid-switch */ }
            };
            _tickTimer.Start();
        }

        capture.ChunkAvailable += chunk =>
        {
            _dsp.AddSamples(chunk);
            _neural?.AddSamples(chunk);
        };
    }

    private void SwitchModel(string name)
    {
        if (!_modelPaths.TryGetValue(name, out var path))
            return;
        var old = _neural;
        _neural = null;
        if (old != null)
        {
            old.ConfirmedChanged -= OnConfirmed;
            old.Dispose();
        }
        var next = new SlidingNeuralChordDetector(path);
        next.ConfirmedChanged += OnConfirmed;
        _neural = next;
        ConfirmedChord = "—"; // fresh ring buffer, wait for the next confirmation
        Strunika.Core.Diagnostics.FileLog.Info(
            $"Live model switched to '{name}' ({System.IO.Path.GetFileName(path)})");
    }

    private void OnProvisional(Chord chord)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            string label = chord.Label == "N" ? "—" : chord.Label;
            if (NeuralAvailable)
            {
                ProvisionalChord = label;
            }
            else
            {
                // No model: the DSP tier is the whole display.
                ConfirmedChord = label;
                if (chord != Chord.None)
                    PushHistory(chord.Label);
            }
        });
    }

    private void OnConfirmed(string rawLabel)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
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

    /// <summary>Chord AI-style: when the model refines its opinion about
    /// the SAME chord (same root — E↔E7, D↔Dm) within a short window,
    /// the last history entry is rewritten in place, keeping its
    /// timestamp. A different root is always a new event — people really
    /// do change chords within a second. Optional (ReviseHistory).</summary>
    private void PushHistory(string label)
    {
        if (label == _lastHistoryLabel)
            return; // the same chord re-confirmed is not a new event
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
        _tickTimer?.Dispose();
        _neural?.Dispose();
    }
}

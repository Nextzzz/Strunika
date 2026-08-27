using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Analysis;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

public partial class PegItem : ObservableObject
{
    public int Index { get; }
    public string Label { get; }
    public string Sub { get; }

    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool isLocked;
    [ObservableProperty] private bool isTuned;

    public PegItem(int index, string label, string sub) { Index = index; Label = label; Sub = sub; }
}

/// <summary>
/// Tuner: YIN on a rolling window → pitch → the string of the selected
/// tuning → deviation in "points" (10 points = one fret = 100 cents).
///
/// The reading is calmed in stages — attack gate (a pluck is sharp and noisy
/// for ~160 ms), YIN clarity gate, median, EMA, slew limit — and the string
/// is chosen once per pluck, never while a note merely decays. A string that
/// stays in tune for 1.5 s is marked tuned; when the last one is, the tuner
/// celebrates and stops listening.
/// </summary>
public partial class TunerViewModel : ObservableObject
{
    private const int WindowSize = 4096;
    private const double InTuneCents = 6.0;
    private const double MaxCentsPerTick = 9.0;       // 80 ms ticks → ~110 cents/s
    private const int MedianTaps = 5;
    private const int PickFrames = 3;                 // frames voted on before choosing a string
    private const double MinClarity = 0.8;             // YIN confidence gate
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan AttackSettle = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan TunedAfter = TimeSpan.FromMilliseconds(1200);
    /// <summary>Short dips out of tune (a wobble, a missed frame) do not restart the clock.</summary>
    private static readonly TimeSpan TunedGrace = TimeSpan.FromMilliseconds(600);

    private readonly IMicrophoneSource _microphone;
    private readonly IProGate _pro;
    private readonly TunerEngine _engine = new(IMicrophoneSource.SampleRate);
    private readonly float[] _buffer = new float[WindowSize * 2];
    private readonly object _lock = new();
    private int _filled;
    private IDispatcherTimer? _timer;

    private readonly Queue<double> _recent = new();
    private double _ema;
    private double _shown;
    private bool _hasReading;
    private int _lastTarget = -1;
    private int _stableTicks;
    private bool _wasInTune;
    private DateTime _lastGoodAt = DateTime.MinValue;
    private DateTime _inTuneSince = DateTime.MinValue;
    private DateTime _lastInTuneAt = DateTime.MinValue;
    private double _envelope;
    private double _noiseFloor = 0.002;
    private double _refPeak;
    private int _dropChunks;
    private DateTime _onsetAt = DateTime.MinValue;
    private bool _reseedAfterAttack;
    private bool _retargetOnNextReading;
    private int _farTicks;
    private readonly List<double> _pendingMidi = new();

    [ObservableProperty] private Tuning tuning = Tuning.Standard;
    [ObservableProperty] private string tuningName = Tuning.Standard.Name;
    [ObservableProperty] private string noteName = "";
    /// <summary>"+3" / "−2" / "0": deviation in points, 10 per fret.</summary>
    [ObservableProperty] private string pointsText = "";
    [ObservableProperty] private double cents;
    [ObservableProperty] private bool hasSignal;
    [ObservableProperty] private bool inTune;
    [ObservableProperty] private bool listening;
    [ObservableProperty] private int lockedIndex = -1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowStartAgainChip))] private bool anyTuned;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowStartAgainChip))] private bool allTuned;
    [ObservableProperty] private string idleMessage = Loc.Get("Tuner_LetsTune");

    /// <summary>"Start again" chip in the top row: once something is tuned,
    /// except when idle with everything tuned — then the main button does it.</summary>
    public bool ShowStartAgainChip => AnyTuned && !(AllTuned && !Listening);

    partial void OnListeningChanged(bool value) => OnPropertyChanged(nameof(ShowStartAgainChip));

    public ObservableCollection<PegItem> Pegs { get; } = new();

    public bool A4Locked => !_pro.Has(Feature.A4Reference);
    public bool AltTuningsLocked => !_pro.Has(Feature.AltTunings);
    public string A4Text => $"A4 · {AppSettings.A4Reference:0} {Loc.Get("Unit_Hz")}";

    public event EventHandler<Feature>? ProRequired;

    /// <summary>A string just got tuned (index) — the view bounces its peg.</summary>
    public event EventHandler<int>? StringTuned;

    /// <summary>The last string just got tuned — the view runs the celebration.</summary>
    public event EventHandler? AllTunedReached;

    /// <summary>Pro users see " Pro" + the wave after the page title.</summary>
    public bool IsPro => _pro.IsPro;

    public TunerViewModel(IMicrophoneSource microphone, IProGate pro)
    {
        _microphone = microphone;
        _pro = pro;
        _pro.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(A4Locked));
            OnPropertyChanged(nameof(IsPro));
            OnPropertyChanged(nameof(AltTuningsLocked));
            if (Tuning.IsPro && AltTuningsLocked)
                ApplyTuning(Tuning.Standard);
        };
        AppSettings.Changed += (_, key) =>
        {
            if (key == nameof(AppSettings.A4Reference)) OnPropertyChanged(nameof(A4Text));
        };
        Loc.Instance.PropertyChanged += (_, _) =>
        {
            TuningName = Tuning.Name;
            OnPropertyChanged(nameof(A4Text));
            UpdateIdleMessage();
        };
        _microphone.ChunkAvailable += OnChunk;

        var initial = Tuning.ById(AppSettings.DefaultTuning);
        ApplyTuning(initial.IsPro && AltTuningsLocked ? Tuning.Standard : initial);
    }

    // ---- tuning & pegs -------------------------------------------------

    public bool TrySelectTuning(Tuning next)
    {
        if (next.IsPro && AltTuningsLocked)
        {
            ProRequired?.Invoke(this, Feature.AltTunings);
            return false;
        }
        ApplyTuning(next);
        return true;
    }

    private void ApplyTuning(Tuning next)
    {
        Tuning = next;
        TuningName = next.Name;
        LockedIndex = -1;
        Pegs.Clear();
        for (int i = 0; i < next.Midi.Length; i++)
            Pegs.Add(new PegItem(i, next.NoteName(i), next.Subscript(i)));
        AnyTuned = AllTuned = false;
        UpdateIdleMessage();
        ResetReading();
    }

    /// <summary>Tap a peg to lock that string; tap the locked peg to go back to auto.</summary>
    [RelayCommand]
    private void TapPeg(PegItem? peg)
    {
        if (peg == null) return;
        LockedIndex = LockedIndex == peg.Index ? -1 : peg.Index;
        foreach (var p in Pegs)
        {
            p.IsLocked = p.Index == LockedIndex;
            p.IsActive = LockedIndex >= 0 ? p.Index == LockedIndex : p.IsActive;
        }
        Haptics.Default.Selection();
        _lastTarget = -1;   // re-seed the filters for the new target
        _inTuneSince = DateTime.MinValue;
        if (LockedIndex >= 0)
            NoteName = Pegs[LockedIndex].Label;
    }

    /// <summary>Forget which strings are tuned (keeps listening state).</summary>
    [RelayCommand]
    private void StartAgain()
    {
        foreach (var p in Pegs) p.IsTuned = false;
        AnyTuned = AllTuned = false;
        _inTuneSince = DateTime.MinValue;
        UpdateIdleMessage();
        Haptics.Default.Selection();
    }

    private void UpdateIdleMessage() =>
        IdleMessage = AllTuned ? Loc.Get("Tuner_AllSet") : Loc.Get("Tuner_LetsTune");

    // ---- microphone ---------------------------------------------------

    /// <summary>Main button: Listen / Stop, or "Start again" once everything is tuned.</summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Listening)
        {
            StopListening();
            return;
        }
        if (AllTuned)
            StartAgain();

        if (!await _microphone.StartAsync())
        {
            IdleMessage = Loc.Get("Tuner_NoMic");
            return;
        }
        Listening = true;
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(80);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Stops the microphone; the locked string is released, tuned
    /// marks stay. Also called when the user leaves the tab.</summary>
    public void StopListening()
    {
        if (!Listening) return;
        _microphone.Stop();
        _timer?.Stop();
        Listening = false;
        LockedIndex = -1;
        foreach (var p in Pegs) p.IsLocked = false;
        ResetReading();
        UpdateIdleMessage();
    }

    private void OnChunk(float[] chunk)
    {
        // Attack detector. A ringing string only ever gets quieter, so any
        // sharp rise of the short-term level (10 ms blocks) against the
        // previous chunk is a new pluck — even while the old note still sounds.
        const int block = 441;
        double fast = 0;
        for (int start = 0; start < chunk.Length; start += block)
        {
            int end = Math.Min(chunk.Length, start + block);
            double sum = 0;
            for (int i = start; i < end; i++) sum += chunk[i] * chunk[i];
            fast = Math.Max(fast, Math.Sqrt(sum / Math.Max(1, end - start)));
        }
        var now = DateTime.Now;
        if (fast > 0.008 && fast > 1.6 * _envelope && now - _onsetAt > TimeSpan.FromMilliseconds(120))
        {
            _onsetAt = now;
            _reseedAfterAttack = true;
        }
        _envelope = fast;
        // Adaptive noise floor: drops at once, creeps up slowly.
        _noiseFloor = fast < _noiseFloor ? fast : Math.Min(_noiseFloor * 1.002 + 2e-6, fast);
        // Mute detector: a damped string loses more than 18 dB almost at once;
        // a freely decaying one never does (once past the attack), even with
        // beating. `_refPeak` is the level just before the drop; three
        // consecutive low chunks confirm it (≈140 ms) — we would rather keep a
        // ringing string on screen a little longer than drop a live one.
        bool settled = now - _onsetAt > TimeSpan.FromMilliseconds(300);
        if (settled && _refPeak > Math.Max(0.002, 3.0 * _noiseFloor) && fast < 0.12 * _refPeak)
        {
            _dropChunks++;
        }
        else
        {
            _dropChunks = 0;
            _refPeak = fast;
        }

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

    private void ResetReading()
    {
        NoteName = LockedIndex >= 0 && LockedIndex < Pegs.Count ? Pegs[LockedIndex].Label : "";
        PointsText = "";
        Cents = 0;
        HasSignal = false;
        InTune = false;
        _wasInTune = false;
        _hasReading = false;
        _recent.Clear();
        _lastTarget = -1;
        _stableTicks = 0;
        _retargetOnNextReading = true;
        _farTicks = 0;
        _pendingMidi.Clear();
        _inTuneSince = DateTime.MinValue;
        _lastInTuneAt = DateTime.MinValue;
        foreach (var p in Pegs)
            p.IsActive = p.Index == LockedIndex;
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

        // Muted string (sudden drop) or real silence: clear at once, long
        // before YIN stops finding a pitch in the residual.
        bool silent = _dropChunks >= 3 || _envelope < Math.Max(0.001, 1.5 * _noiseFloor);
        double clarity = 0;
        double? fundamental = silent ? null : _engine.DetectFundamental(window, out clarity);
        // Hysteresis: a note needs clarity 0.8 to appear, only 0.6 to stay —
        // a decaying string's clarity wavers while it is still clearly sounding.
        double minClarity = _hasReading ? MinClarity - 0.2 : MinClarity;
        if (fundamental == null || clarity < minClarity)
        {
            if (silent && _hasReading)
                ResetReading();          // muted: clear right away, no hold
            // The note may simply have decayed while sitting in tune: still count it.
            CheckTuned();
            if (_hasReading && DateTime.Now - _lastGoodAt > Hold)
                ResetReading();
            return;
        }
        _lastGoodAt = DateTime.Now;

        // Let the pluck settle before judging it.
        if (DateTime.Now - _onsetAt < AttackSettle)
            return;
        if (_reseedAfterAttack)
        {
            _reseedAfterAttack = false;
            _retargetOnNextReading = true;   // a new pluck may be a different string
            _pendingMidi.Clear();
            _recent.Clear();
            _ema = double.NaN;               // re-seed the EMA below
            // A re-pluck of the same string keeps the in-tune clock running
            // (the grace period below covers the attack gap).
        }

        // Pitch → MIDI against the user's A4, then the target string.
        // Distances are measured modulo octaves so a decaying note's 2nd
        // harmonic (G3 → G4) stays on its string.
        double midi = TunerEngine.MidiOf(fundamental.Value, AppSettings.A4Reference);
        int target;
        if (LockedIndex >= 0)
        {
            target = LockedIndex;
        }
        else if (_lastTarget < 0 || _retargetOnNextReading)
        {
            // Choosing the string from a single frame is fragile (the first
            // frames after a pluck often carry a harmonic): collect a few and
            // decide on their median.
            _pendingMidi.Add(midi);
            if (_pendingMidi.Count < PickFrames)
                return;
            double picked = _pendingMidi.OrderBy(v => v).ElementAt(_pendingMidi.Count / 2);
            _pendingMidi.Clear();
            target = TunerEngine.NearestString(picked, Tuning.Midi);
        }
        else
        {
            // Fallback for a pluck the level detector missed (soft attack on a
            // muted string): a clearly different note, held for ~4 ticks.
            target = _lastTarget;
            int best = TunerEngine.NearestString(midi, Tuning.Midi);
            bool far = Math.Abs(midi - Tuning.Midi[_lastTarget]) > 1.5
                && Math.Abs(TunerEngine.FoldedSemitones(midi, Tuning.Midi[_lastTarget])) > 1.5;
            _farTicks = best != _lastTarget && far ? _farTicks + 1 : 0;
            if (_farTicks >= 4)
            {
                target = best;
                _farTicks = 0;
            }
        }
        _retargetOnNextReading = false;
        double raw = TunerEngine.FoldedSemitones(midi, Tuning.Midi[target]) * 100.0;

        if (target != _lastTarget)
        {
            _recent.Clear();
            _ema = raw;
            _shown = Math.Clamp(raw, -50, 50);
            _lastTarget = target;
            _stableTicks = 0;
            _inTuneSince = DateTime.MinValue;
            foreach (var p in Pegs)
                p.IsActive = p.Index == target;
            NoteName = Pegs[target].Label;
        }
        else if (double.IsNaN(_ema))
        {
            _ema = raw;   // same string, fresh pluck
        }

        // 1. median kills single-frame outliers · 2. EMA smooths · 3. slew limits speed
        _recent.Enqueue(raw);
        while (_recent.Count > MedianTaps) _recent.Dequeue();
        double median = _recent.OrderBy(v => v).ElementAt(_recent.Count / 2);
        _ema = 0.6 * _ema + 0.4 * median;
        double goal = Math.Clamp(_ema, -50, 50);
        _shown += Math.Clamp(goal - _shown, -MaxCentsPerTick, MaxCentsPerTick);

        _hasReading = true;
        HasSignal = true;
        Cents = _shown;

        bool nowInTune = Math.Abs(_ema) <= InTuneCents;
        _stableTicks = nowInTune ? _stableTicks + 1 : 0;
        InTune = nowInTune && _stableTicks >= 2;
        if (InTune && !_wasInTune)
            Haptics.Default.Success();
        _wasInTune = InTune;

        int points = (int)Math.Round(_ema / 10.0);
        PointsText = points == 0 ? "0" : points > 0 ? $"+{points}" : $"−{-points}";

        // Held in tune long enough → this string is done. Brief wobbles are forgiven.
        var now2 = DateTime.Now;
        if (nowInTune)
        {
            if (_inTuneSince == DateTime.MinValue)
                _inTuneSince = now2;
            _lastInTuneAt = now2;
        }
        else if (_inTuneSince != DateTime.MinValue && now2 - _lastInTuneAt > TunedGrace)
        {
            _inTuneSince = DateTime.MinValue;
        }
        CheckTuned();
    }

    private void CheckTuned()
    {
        if (_inTuneSince == DateTime.MinValue || _lastTarget < 0 || _lastTarget >= Pegs.Count) return;
        var now = DateTime.Now;
        if (now - _lastInTuneAt > TunedGrace) return;          // not in tune right now
        if (now - _inTuneSince < TunedAfter) return;
        if (!Pegs[_lastTarget].IsTuned)
            MarkTuned(_lastTarget);
    }

    private void MarkTuned(int index)
    {
        Pegs[index].IsTuned = true;
        AnyTuned = true;
        Haptics.Default.Success();
        StringTuned?.Invoke(this, index);
        if (Pegs.All(p => p.IsTuned))
        {
            AllTuned = true;
            UpdateIdleMessage();
            AllTunedReached?.Invoke(this, EventArgs.Empty);
        }
    }

}

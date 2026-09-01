using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Data;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Neural;

namespace Strunika.Mobile.ViewModels;

/// <summary>
/// One song on screen: the chord conveyor follows the transport (file player
/// or YouTube embed) frame by frame, the panel above it shows the chord that
/// is playing and the one coming next with their diagrams, plus metronome
/// clicks on the analysed beats, simple chords, and the Pro tools
/// (capo/transpose, speed, A–B).
/// </summary>
public sealed partial class SongViewModel : ObservableObject
{
    private static readonly double[] Speeds = { 1.0, 0.75, 0.5, 1.25 };
    /// <summary>How often the transport is asked where it really is; between
    /// probes the position is predicted so the conveyor moves smoothly. While
    /// a start is pending we ask much more often, so the conveyor leaves the
    /// moment the player does.</summary>
    private const double ProbeSeconds = 0.20, StartingProbeSeconds = 0.04;

    private readonly ISongRepository _songs;
    private readonly IProGate _pro;
    private readonly IClickPlayer _click;
    /// <summary>Position the player chose for a chord's diagram, by label.</summary>
    private readonly Dictionary<string, int> _shapeChoice = new();
    private IMediaTransport? _transport;
    private IReadOnlyList<ChordSegmentDto> _raw = Array.Empty<ChordSegmentDto>();
    private double[] _beats = Array.Empty<double>();
    private int _nextBeat;
    private bool _scrubbing, _wasPlaying, _probing;
    private double _predicted, _sinceProbe, _lastProbe = -1;
    // After a seek the transport keeps reporting the old time for a probe or two
    // (YouTube seeks asynchronously; a probe in flight predates the seek). Until
    // the player confirms the target, its position is not adopted.
    private double _seekTarget = -1;
    private long _seekSequence, _seekTicks;

    private void NoteSeek(double target)
    {
        _seekTarget = target;
        _seekTicks = Environment.TickCount64;
        _seekSequence++;
    }
    private int _lastSecond = -1, _currentIndex = -1;
    private long _lastPrevPress;

    public SongViewModel(Song song, ISongRepository songs, IProGate pro, IClickPlayer click)
    {
        Song = song;
        _songs = songs;
        _pro = pro;
        _click = click;
        _raw = song.Segments;
        _beats = song.Beats;
        Peaks = song.Peaks;
        Duration = song.DurationSec;
        _click.Volume = ClickVolume;
        _pro.Changed += OnProChanged;                            // unhooked in Dispose: the gate outlives every song
        Rebuild();
    }

    public Song Song { get; }
    public bool IsPro => _pro.IsPro;
    public bool IsYouTube => Song.Source == SongSource.YouTube;
    public string Title => Song.Title;
    public string Artist => string.IsNullOrWhiteSpace(Song.Artist) ? Loc.Get(Song.Source == SongSource.Recording ? "Library_Source_Recording" : "Library_Source_File") : Song.Artist;
    /// <summary>The key as it sounds now (transposition included).</summary>
    public string KeyText => Song.Key is { Length: > 0 } key
        ? (TransposeSteps == 0 ? key : ChordLabels.Transpose(key, TransposeSteps))
        : "—";
    public string TempoText => Song.Bpm > 0 ? $"♩ {Song.Bpm:0} · {BeatsPerBar}/4" : "";
    /// <summary>Beats in a bar. The analysis only reports 4/4 so far; the beat
    /// grid breaks its rows on this, so it is a property, not a literal.</summary>
    public int BeatsPerBar => 4;

    /// <summary>The key the song was analysed in, whatever the tools are set to.</summary>
    public string OriginalKeyText => Song.Key is { Length: > 0 } key ? key : "—";
    /// <summary>False when the song has no key at all — then there is nothing to show.</summary>
    public bool HasKey => Song.Key is { Length: > 0 };

    /// <summary>"Файл · C · ♩ 99 · 4/4" under the title — a fact about the song,
    /// so it keeps the original key even while the chords are transposed.</summary>
    public string SubtitleText
    {
        get
        {
            var parts = new List<string> { Artist };
            if (Song.Key is { Length: > 0 } key) parts.Add(key);
            if (TempoText.Length > 0) parts.Add(TempoText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Every chord in the song, in the order it first appears, as shown.</summary>
    public IReadOnlyList<string> ChordList
    {
        get
        {
            var seen = new List<string>();
            foreach (var seg in Segments)
                if (seg.Label != "—" && !seen.Contains(seg.Label)) seen.Add(seg.Label);
            return seen;
        }
    }

    /// <summary>The speed choices offered by the picker.</summary>
    public List<string> SpeedOptions { get; } = Speeds.OrderBy(v => v).Select(v => $"{v:0.0#}×").ToList();

    public string SelectedSpeed
    {
        get => $"{Speed:0.0#}×";
        set
        {
            int i = SpeedOptions.IndexOf(value);
            if (i < 0) return;
            double picked = Speeds.OrderBy(v => v).ElementAt(i);
            if (Math.Abs(picked - Speed) < 1e-6) return;
            if (SpeedLocked) { ProRequired?.Invoke(this, Feature.Speed); OnPropertyChanged(nameof(SelectedSpeed)); return; }
            Speed = picked;
            _ = ApplySpeedAsync();
        }
    }

    [ObservableProperty] private IReadOnlyList<ChordSegmentDto> _segments = Array.Empty<ChordSegmentDto>();
    [ObservableProperty] private double[] _beatTimes = Array.Empty<double>();
    [ObservableProperty] private byte[] _peaks = Array.Empty<byte>();
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _isPlaying;
    /// <summary>Play was asked for and the player has not started yet.</summary>
    [ObservableProperty] private bool _starting;
    [ObservableProperty] private bool _canPlay = true;
    [ObservableProperty] private string _currentChord = "—";
    [ObservableProperty] private string _nextChord = "";
    [ObservableProperty] private ChordShape? _currentShape;
    [ObservableProperty] private ChordShape? _nextShape;
    [ObservableProperty] private bool _simpleChords = AppSettings.SimpleChords;
    [ObservableProperty] private bool _metronome;
    [ObservableProperty] private int _capo;
    [ObservableProperty] private int _transposeSteps;
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private double _loopStart = -1;
    [ObservableProperty] private double _loopEnd = -1;
    [ObservableProperty] private double _volume = AppSettings.SongVolume;
    [ObservableProperty] private double _clickVolume = AppSettings.ClickVolume;
    [ObservableProperty] private bool _playerExpanded;
    [ObservableProperty] private bool _leftHanded = AppSettings.LeftHanded;

    public string PositionText => SongItem.Duration(Position);
    public string DurationText => SongItem.Duration(Duration);
    public double SliderMax => Math.Max(1, Duration);
    public string CapoText => string.Format(Loc.Get("Song_Capo"), Capo);
    public string SpeedText => $"{Speed:0.0#}×";
    public bool HasLoop => LoopStart >= 0 && LoopEnd > LoopStart;
    public string LoopText => HasLoop ? "A–B ✓" : "A–B";
    /// <summary>The stepper's readout: semitones from the original, 0 / +1 / -2.</summary>
    public string TransposeText => TransposeSteps == 0 ? "0" : $"{TransposeSteps:+0;-0}";
    public string CapoNumberText => Capo.ToString();
    public bool HasCapo => Capo > 0;
    /// <summary>The capo that would turn the most chords into open shapes. It
    /// follows the chords as they are shown — transposed and simplified — so the
    /// advice matches what the player actually reads.</summary>
    public int SuggestedCapo => ChordShapes.SuggestCapo(Segments.Select(s => s.Label));
    public string CapoSuggestionText => string.Format(Loc.Get("Song_Capo_Suggest"), SuggestedCapo);
    public bool HasCapoSuggestion => SuggestedCapo != Capo;
    public bool CapoLocked => !_pro.Has(Feature.TransposeCapo);
    public bool SpeedLocked => !_pro.Has(Feature.Speed);
    public bool LoopLocked => !_pro.Has(Feature.ABLoop);
    public bool HasNext => NextChord.Length > 0;
    /// <summary>The button reads "pause" from the moment play is asked for.</summary>
    public bool ShowPause => IsPlaying || Starting;

    /// <summary>A transport probe is in flight (frame diagnostics).</summary>
    public bool IsProbing => _probing;
    public string TransportKind => _transport is YouTubeTransport ? "youtube" : _transport is FileTransport ? "file" : "none";

    public event EventHandler<Feature>? ProRequired;
    public event EventHandler<string>? Message;

    // ---- transport ----------------------------------------------------

    public void Attach(IMediaTransport transport)
    {
        _transport?.Dispose();
        _transport = transport;
        _predicted = Position;
        CanPlay = transport.IsReady;                             // YouTube: off until its page answers
        _ = transport.SetVolumeAsync(Volume);
    }

    /// <summary>
    /// Called once per rendered frame. The position is predicted from the
    /// clock and only corrected against the transport every
    /// <see cref="ProbeSeconds"/> — polling a WebView or a wave player 60
    /// times a second is both slow and jittery.
    /// </summary>
    public void Frame(double dt)
    {
        if (_transport == null || _scrubbing) return;
        if (IsPlaying) _predicted = Math.Clamp(_predicted + dt * Speed, 0, Math.Max(0, Duration));
        _sinceProbe += dt;
        if (_sinceProbe >= (Starting ? StartingProbeSeconds : ProbeSeconds) && !_probing)
        {
            _sinceProbe = 0;
            _ = ProbeAsync();
        }
        SetPosition(_predicted, fromTransport: true);
    }

    private async Task ProbeAsync()
    {
        // Keep our own reference: closing the page disposes the transport and
        // nulls the field, and every await here is a chance for that to happen.
        var transport = _transport;
        if (transport == null) return;
        _probing = true;
        try
        {
            long sequence = _seekSequence;
            var (pos, dur, playing, pending) = await transport.PollAsync();
            if (_transport != transport) return;                 // the page moved on
            CanPlay = transport.IsReady;
            if (dur > 0 && Math.Abs(dur - Duration) > 0.5) Duration = dur;
            if (sequence != _seekSequence) return;               // answered a question asked before a seek
            IsPlaying = playing;
            Starting = pending;
            if (_scrubbing) return;
            if (_seekTarget >= 0)
            {
                bool confirmed = Math.Abs(pos - _seekTarget) < 0.5 || Environment.TickCount64 - _seekTicks > 1500;
                if (!confirmed) return;                          // still the old time: keep the prediction
                _seekTarget = -1;
            }
            if (HasLoop && playing && pos >= LoopEnd)
            {
                await transport.SeekAsync(LoopStart);
                NoteSeek(LoopStart);
                _predicted = LoopStart;
                _nextBeat = NextBeatAfter(LoopStart);
                return;
            }
            // A player that says "playing" but reports the same position twice
            // is buffering (YouTube does this for the first half second): hold
            // the conveyor there instead of running ahead and snapping back.
            bool stalled = playing && Math.Abs(pos - _lastProbe) < 1e-3;
            _lastProbe = pos;
            // Snap on a real jump, otherwise ease the prediction towards the
            // truth so the conveyor never visibly steps.
            if (!playing || stalled || Math.Abs(pos - _predicted) > 0.35) _predicted = pos;
            else _predicted += (pos - _predicted) * 0.25;
        }
        catch (Exception ex) { FileLog.Error("song probe", ex); }
        finally { _probing = false; }
    }

    private void SetPosition(double pos, bool fromTransport)
    {
        if (Metronome && fromTransport && IsPlaying)
        {
            while (_nextBeat < _beats.Length && _beats[_nextBeat] <= pos)
            {
                // Only click for beats we are actually crossing now (not after a seek).
                if (pos - _beats[_nextBeat] < 0.25) _click.Click(_nextBeat % 4 == 0);
                _nextBeat++;
            }
        }
        Position = pos;
    }

    partial void OnPositionChanged(double value)
    {
        int second = (int)value;
        if (second != _lastSecond) { _lastSecond = second; OnPropertyChanged(nameof(PositionText)); }
        UpdateChords(value);
    }

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(SliderMax));
    }

    [RelayCommand]
    private async Task TogglePlayAsync()
    {
        var transport = _transport;
        if (transport == null || !CanPlay) return;
        if (IsPlaying || Starting)
        {
            Starting = false;
            await transport.PauseAsync();
        }
        else
        {
            _nextBeat = NextBeatAfter(Position);
            if (Duration > 0 && Position >= Duration - 0.2) { await transport.SeekAsync(0); NoteSeek(0); _predicted = 0; }
            Starting = true;                                    // the conveyor waits for the player's word
            await transport.PlayAsync();
        }
        Haptics.Default.Selection();
    }

    public async Task PauseAsync()
    {
        var transport = _transport;
        if (transport == null || !IsPlaying) return;
        await transport.PauseAsync();
        IsPlaying = false;
    }

    /// <summary>« — back to the start of this chord; pressed again within 1.5 s
    /// (or right after the chord began) it steps to the previous one, the way
    /// track skip behaves.</summary>
    [RelayCommand]
    private Task PrevChordAsync()
    {
        long now = Environment.TickCount64;
        bool again = now - _lastPrevPress < 1500;
        _lastPrevPress = now;
        var segs = Segments;
        int i = IndexAt(Position);
        double target = 0;
        if (i > 0 && (again || Position - segs[i].Start < 1.0)) target = segs[i - 1].Start;
        else if (i >= 0) target = segs[i].Start;
        return SeekAsync(target);
    }

    /// <summary>» — to the start of the next chord.</summary>
    [RelayCommand]
    private Task NextChordAsync()
    {
        var segs = Segments;
        int i = IndexAt(Position);
        if (i >= 0 && i + 1 < segs.Count) return SeekAsync(segs[i + 1].Start);
        foreach (var seg in segs)
            if (seg.Start > Position) return SeekAsync(seg.Start);
        return SeekAsync(Duration);
    }

    public async Task SeekAsync(double seconds)
    {
        var transport = _transport;
        if (transport == null) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, Duration));
        await transport.SeekAsync(seconds);
        NoteSeek(seconds);
        _predicted = seconds;
        _nextBeat = NextBeatAfter(seconds);
        SetPosition(seconds, fromTransport: false);
    }

    /// <summary>Drag on the conveyor or the slider: silent until the finger lifts.</summary>
    public async Task ScrubStartAsync()
    {
        var transport = _transport;
        if (transport == null || _scrubbing) return;
        _scrubbing = true;
        _seekSequence++;                                         // probes already in flight are about the old place
        _wasPlaying = IsPlaying;
        if (_wasPlaying) await transport.PauseAsync();
    }

    public void Scrubbing(double seconds) => SetPosition(seconds, fromTransport: false);

    public async Task ScrubEndAsync(double seconds)
    {
        var transport = _transport;
        if (transport == null) { _scrubbing = false; return; }
        seconds = Math.Clamp(seconds, 0, Math.Max(0, Duration));
        await transport.SeekAsync(seconds);
        NoteSeek(seconds);
        _predicted = seconds;
        _nextBeat = NextBeatAfter(seconds);
        _scrubbing = false;
        if (_wasPlaying) await transport.PlayAsync();
    }

    private int NextBeatAfter(double pos)
    {
        int i = Array.BinarySearch(_beats, pos);
        return i < 0 ? ~i : i + 1;
    }

    // ---- chords -------------------------------------------------------

    partial void OnSimpleChordsChanged(bool value)
    {
        AppSettings.SimpleChords = value;                        // one value for the whole app
        _shapeChoice.Clear();
        Rebuild();
    }
    partial void OnTransposeStepsChanged(int value)
    {
        _shapeChoice.Clear();
        OnPropertyChanged(nameof(KeyText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(TransposeText));
        Rebuild();
    }
    partial void OnCapoChanged(int value)
    {
        _shapeChoice.Clear();
        OnPropertyChanged(nameof(CapoText));
        OnPropertyChanged(nameof(CapoNumberText));
        OnPropertyChanged(nameof(HasCapo));
        OnPropertyChanged(nameof(CapoSuggestionText));
        OnPropertyChanged(nameof(HasCapoSuggestion));
        RefreshShapes();
    }
    partial void OnSpeedChanged(double value) { OnPropertyChanged(nameof(SpeedText)); OnPropertyChanged(nameof(SelectedSpeed)); }

    partial void OnClickVolumeChanged(double value)
    {
        AppSettings.ClickVolume = value;
        _click.Volume = value;
    }

    partial void OnVolumeChanged(double value)
    {
        AppSettings.SongVolume = value;
        _ = (_transport?.SetVolumeAsync(value) ?? Task.CompletedTask);
    }
    partial void OnLoopStartChanged(double value) { OnPropertyChanged(nameof(HasLoop)); OnPropertyChanged(nameof(LoopText)); }
    partial void OnLoopEndChanged(double value) { OnPropertyChanged(nameof(HasLoop)); OnPropertyChanged(nameof(LoopText)); }
    partial void OnNextChordChanged(string value) => OnPropertyChanged(nameof(HasNext));
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(ShowPause));
    partial void OnStartingChanged(bool value) => OnPropertyChanged(nameof(ShowPause));

    private void Rebuild()
    {
        Segments = _raw.Select(s =>
        {
            string label = s.Label;
            if (label != "—")
            {
                if (SimpleChords) label = ChordLabels.Simplify(label);
                if (TransposeSteps != 0) label = ChordLabels.Transpose(label, TransposeSteps);
            }
            return new ChordSegmentDto(s.Start, s.End, label);
        }).ToList();
        BeatTimes = _beats;
        _currentIndex = -1;
        OnPropertyChanged(nameof(ChordList));
        OnPropertyChanged(nameof(SuggestedCapo));                // the advice follows the transposition
        OnPropertyChanged(nameof(CapoSuggestionText));
        OnPropertyChanged(nameof(HasCapoSuggestion));
        UpdateChords(Position);
    }

    /// <summary>The segment playing at <paramref name="pos"/>, scanning from the
    /// last one — this runs on every frame.</summary>
    private int IndexAt(double pos)
    {
        var segs = Segments;
        if (segs.Count == 0) return -1;
        if (_currentIndex >= 0 && _currentIndex < segs.Count && pos >= segs[_currentIndex].Start && pos < segs[_currentIndex].End)
            return _currentIndex;
        for (int k = 0; k < segs.Count; k++)
            if (pos >= segs[k].Start && pos < segs[k].End) return k;
        return -1;
    }

    private void UpdateChords(double pos)
    {
        var segs = Segments;
        int i = IndexAt(pos);
        _currentIndex = i;
        var current = i >= 0 ? segs[i].Label : "—";
        string next = "";
        for (int k = i < 0 ? 0 : i + 1; k < segs.Count; k++)
            if (segs[k].Label != "—" && segs[k].Label != current) { next = segs[k].Label; break; }
        if (current != CurrentChord)
        {
            CurrentChord = current;
            CurrentShape = ShapeFor(current);
        }
        if (next != NextChord)
        {
            NextChord = next;
            NextShape = ShapeFor(next);
        }
    }

    private void RefreshShapes()
    {
        CurrentShape = ShapeFor(CurrentChord);
        NextShape = ShapeFor(NextChord);
    }

    private ChordShape? ShapeFor(string label)
    {
        var positions = ChordShapes.Positions(label, Capo);
        if (positions.Count == 0) return null;
        int index = _shapeChoice.TryGetValue(label, out var chosen) ? Math.Clamp(chosen, 0, positions.Count - 1) : 0;
        return positions[index];
    }

    /// <summary>All positions for a chord, and which one is on screen — the
    /// alternative-shapes sheet works with these.</summary>
    public (IReadOnlyList<ChordShape> Positions, int Index) ShapeChoices(string label)
    {
        var positions = ChordShapes.Positions(label, Capo);
        int index = _shapeChoice.TryGetValue(label, out var chosen) ? Math.Clamp(chosen, 0, Math.Max(0, positions.Count - 1)) : 0;
        return (positions, index);
    }

    public void ChooseShape(string label, int index)
    {
        _shapeChoice[label] = index;
        RefreshShapes();
    }

    // ---- tools --------------------------------------------------------

    [RelayCommand]
    private void ToggleSimple() => SimpleChords = !SimpleChords;

    [RelayCommand]
    private void ToggleMetronome()
    {
        Metronome = !Metronome;
        _nextBeat = NextBeatAfter(Position);
        if (Metronome && _beats.Length == 0)
            Message?.Invoke(this, Loc.Get("Song_NoBeats"));
    }

    private async Task ApplySpeedAsync()
    {
        var transport = _transport;
        if (transport == null) return;
        await transport.SetRateAsync(Speed);
        if (!transport.SupportsRate)
            Message?.Invoke(this, Loc.Get("Song_SpeedUnsupported"));
    }

    [RelayCommand]
    private void ToggleLoop()
    {
        if (LoopLocked) { ProRequired?.Invoke(this, Feature.ABLoop); return; }
        if (LoopStart < 0) { LoopStart = Position; LoopEnd = -1; }
        else if (LoopEnd < 0) { if (Position > LoopStart + 0.5) LoopEnd = Position; else LoopStart = -1; }
        else { LoopStart = -1; LoopEnd = -1; }
        Haptics.Default.Selection();
    }

    [RelayCommand]
    private void CapoUp()
    {
        if (!RequestCapo()) return;
        if (Capo < 11) Capo++;
    }

    [RelayCommand]
    private void CapoDown()
    {
        if (!RequestCapo()) return;
        if (Capo > 0) Capo--;
    }

    /// <summary>Take the suggested capo (the "smart capo").</summary>
    [RelayCommand]
    private void UseSuggestedCapo()
    {
        if (!RequestCapo()) return;
        Capo = SuggestedCapo;
    }

    [RelayCommand]
    private void TransposeUp()
    {
        if (!RequestCapo()) return;
        if (TransposeSteps < 11) TransposeSteps++;
    }

    [RelayCommand]
    private void TransposeDown()
    {
        if (!RequestCapo()) return;
        if (TransposeSteps > -11) TransposeSteps--;
    }

    public bool RequestCapo()
    {
        if (CapoLocked) { ProRequired?.Invoke(this, Feature.TransposeCapo); return false; }
        return true;
    }

    /// <summary>Songs analysed before M3 carry no waveform: draw one now (local
    /// sources only — YouTube audio is never kept) and remember it.</summary>
    public async Task EnsurePeaksAsync(IAudioDecoder decoder)
    {
        bool current = Peaks.Length > 0 && Song.PeaksVersion == Strunika.Core.Audio.Waveform.Version;
        if (current || IsYouTube || string.IsNullOrEmpty(Song.SourceRef)) return;
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, Song.SourceRef);
            if (!File.Exists(path)) return;
            var samples = await decoder.DecodeMonoAsync(path, 44100, CancellationToken.None);
            var peaks = Strunika.Core.Audio.Waveform.Peaks(samples, 44100);
            Song.Peaks = peaks;
            Song.PeaksVersion = Strunika.Core.Audio.Waveform.Version;
            await _songs.UpdateAsync(Song);
            Peaks = peaks;
        }
        catch (Exception ex) { FileLog.Error("song peaks", ex); }
    }

    public async Task ToggleFavouriteAsync()
    {
        Song.Favourite = !Song.Favourite;
        await _songs.UpdateAsync(Song);
        OnPropertyChanged(nameof(Song));
    }

    private void OnProChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsPro));

    public void Dispose()
    {
        _pro.Changed -= OnProChanged;
        try { _transport?.Dispose(); } catch (Exception ex) { FileLog.Error("song dispose", ex); }
        _transport = null;
    }
}

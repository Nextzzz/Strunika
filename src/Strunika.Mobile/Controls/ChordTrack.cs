using Strunika.Mobile.Theme;
using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The conveyor: the song's waveform under a fixed playhead, every chord a
/// pill at the moment it starts, the next chord pinned to the right edge until
/// it enters the frame. Tap a pill = seek to that chord, drag = scrub.
/// <para>
/// Nothing is drawn per frame. The ribbon (bars, beat ruler, pills) is
/// rendered into canvases three screens wide once every few seconds and only
/// <i>translated</i> as the song plays. Bars change colour at the playhead
/// without any drawing: the ribbon exists twice, once with "played" bars and
/// once with "coming" bars, each inside a container clipped to its side of the
/// playhead, both sliding together. Each variant is double-buffered — a new
/// window is rendered into the spare canvas and shown only once its Draw has
/// run, otherwise the compositor flashes the old content at the new offset for
/// a frame. Redrawing all of this sixty times a second made the Windows XAML
/// runtime induce a full garbage collection about once a second (a ~100 ms
/// stall); on a phone it would have burnt battery for nothing.
/// </para>
/// </summary>
public sealed class ChordTrack : Grid
{
    private static void Redraw(BindableObject b, object? o, object? n) => ((ChordTrack)b).Redraw();
    private static void Rebuild(BindableObject b, object? o, object? n) { var t = (ChordTrack)b; t._bars = null; t.ResetBuffers(); t.Follow(); }
    private static void Reloop(BindableObject b, object? o, object? n) => ((ChordTrack)b).UpdateLoop();

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(nameof(Position), typeof(double), typeof(ChordTrack), 0.0, propertyChanged: (b, _, _) => ((ChordTrack)b).Follow());
    public static readonly BindableProperty DurationProperty = BindableProperty.Create(nameof(Duration), typeof(double), typeof(ChordTrack), 0.0);
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(ChordTrack), null, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t._currentIndex = -1; t._nextIndex = -1; t.Redraw(); t.Follow(); t.ChordsArrived(); });
    public static readonly BindableProperty BeatsProperty = BindableProperty.Create(nameof(Beats), typeof(double[]), typeof(ChordTrack), null, propertyChanged: (b, _, _) => { ((ChordTrack)b)._clocks.Clear(); ((ChordTrack)b).Redraw(); });
    public static readonly BindableProperty PeaksProperty = BindableProperty.Create(nameof(Peaks), typeof(byte[]), typeof(ChordTrack), null, propertyChanged: Rebuild);
    public static readonly BindableProperty PeaksFpsProperty = BindableProperty.Create(nameof(PeaksFps), typeof(int), typeof(ChordTrack), 40, propertyChanged: Rebuild);
    public static readonly BindableProperty PixelsPerSecondProperty = BindableProperty.Create(nameof(PixelsPerSecond), typeof(double), typeof(ChordTrack), 93.0, propertyChanged: Rebuild);
    public static readonly BindableProperty LoopStartProperty = BindableProperty.Create(nameof(LoopStart), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Reloop);
    public static readonly BindableProperty LoopEndProperty = BindableProperty.Create(nameof(LoopEnd), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Reloop);
    /// <summary>The start is set and the end is not: the band grows from the
    /// start to the playhead as the song runs, so it is plain that the loop is
    /// being taken.</summary>
    /// <summary>The editor is on: a chord is a block over the time it lasts,
    /// with a hold on it, instead of a badge at the moment it starts.</summary>
    public static readonly BindableProperty EditingProperty = BindableProperty.Create(nameof(Editing), typeof(bool), typeof(ChordTrack), false, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; if (!t.Editing) t.Land(); t.Redraw(); t.Follow(); });
    /// <summary>Which chord is being worked on, by its place in the song.</summary>
    public static readonly BindableProperty SelectedProperty = BindableProperty.Create(nameof(Selected), typeof(int), typeof(ChordTrack), -1, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t.Redraw(); t.Follow(); });
    public static readonly BindableProperty LoopArmedProperty = BindableProperty.Create(nameof(LoopArmed), typeof(bool), typeof(ChordTrack), false, propertyChanged: Reloop);
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(nameof(Accent), typeof(Color), typeof(ChordTrack), Colors.Goldenrod, propertyChanged: (b, _, _) => ((ChordTrack)b).ApplyColours());
    public static readonly BindableProperty OnAccentProperty = BindableProperty.Create(nameof(OnAccent), typeof(Color), typeof(ChordTrack), Colors.Black, propertyChanged: (b, _, _) => ((ChordTrack)b).ApplyColours());
    public static readonly BindableProperty WaveColorProperty = BindableProperty.Create(nameof(WaveColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PillColorProperty = BindableProperty.Create(nameof(PillColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PinnedColorProperty = BindableProperty.Create(nameof(PinnedColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PinnedTextColorProperty = BindableProperty.Create(nameof(PinnedTextColor), typeof(Color), typeof(ChordTrack), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(ChordTrack), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(ChordTrack), Colors.Gray, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t._beatTick = null; t._barTick = null; t.Redraw(); });
    /// <summary>The ground the track sits on: what a chord taken off it leaves behind.</summary>
    public static readonly BindableProperty BackdropColorProperty = BindableProperty.Create(nameof(BackdropColor), typeof(Color), typeof(ChordTrack), Colors.Black, propertyChanged: (b, _, _) => ((ChordTrack)b).ApplyColours());

    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public IReadOnlyList<ChordSegmentDto>? Segments { get => (IReadOnlyList<ChordSegmentDto>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public double[]? Beats { get => (double[]?)GetValue(BeatsProperty); set => SetValue(BeatsProperty, value); }
    public byte[]? Peaks { get => (byte[]?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public int PeaksFps { get => (int)GetValue(PeaksFpsProperty); set => SetValue(PeaksFpsProperty, value); }
    /// <summary>The zoom: a fixed scale, so a wider screen shows more of the song.</summary>
    public double PixelsPerSecond { get => (double)GetValue(PixelsPerSecondProperty); set => SetValue(PixelsPerSecondProperty, value); }
    public double LoopStart { get => (double)GetValue(LoopStartProperty); set => SetValue(LoopStartProperty, value); }
    public double LoopEnd { get => (double)GetValue(LoopEndProperty); set => SetValue(LoopEndProperty, value); }
    public bool LoopArmed { get => (bool)GetValue(LoopArmedProperty); set => SetValue(LoopArmedProperty, value); }
    public bool Editing { get => (bool)GetValue(EditingProperty); set => SetValue(EditingProperty, value); }
    public int Selected { get => (int)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Color OnAccent { get => (Color)GetValue(OnAccentProperty); set => SetValue(OnAccentProperty, value); }
    public Color WaveColor { get => (Color)GetValue(WaveColorProperty); set => SetValue(WaveColorProperty, value); }
    public Color PillColor { get => (Color)GetValue(PillColorProperty); set => SetValue(PillColorProperty, value); }
    /// <summary>The chord waiting at the right edge is not yet on the ribbon —
    /// it reads as a preview, not as a marker in place.</summary>
    public Color PinnedColor { get => (Color)GetValue(PinnedColorProperty); set => SetValue(PinnedColorProperty, value); }
    public Color PinnedTextColor { get => (Color)GetValue(PinnedTextColorProperty); set => SetValue(PinnedTextColorProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public Color BackdropColor { get => (Color)GetValue(BackdropColorProperty); set => SetValue(BackdropColorProperty, value); }

    /// <summary>Finger down: the owner pauses.</summary>
    public event EventHandler? ScrubStarted;
    /// <summary>Dragging (the owner applies the value).</summary>
    public event EventHandler<double>? Scrubbing;
    /// <summary>Finger up: seek here and resume if it was playing.</summary>
    public event EventHandler<double>? ScrubEnded;
    /// <summary>A tap: the start of the chord that was tapped, or the time under the finger.</summary>
    public event EventHandler<double>? SeekRequested;
    /// <summary>A chord on the track was tapped: the owner makes it the one
    /// being worked on.</summary>
    public event EventHandler<int>? SelectionRequested;
    /// <summary>Where the chord was let go — its new place in the song.</summary>
    public event EventHandler<(int Index, double Start, double End)>? SegmentMoved;
    /// <summary>A loop end was taken hold of: the owner pauses and stays paused.</summary>
    public event EventHandler? LoopEditStarted;
    /// <summary>The loop while an end is being dragged; the owner applies it and it comes back.</summary>
    public event EventHandler<(double Start, double End)>? LoopChanging;
    /// <summary>The end was let go, at this position (the view may have scrolled under it).</summary>
    public event EventHandler<double>? LoopEditEnded;

    /// <summary>The room the track keeps over its pills, for whoever lays out what is above it.</summary>
    public static double TopSpace => PillTop;

    /// <summary>Where the playhead sits across the width: a quarter in, so most
    /// of the track is the music still to come.</summary>
    private const float PlayheadAt = 0.25f;
    private const float BarWidth = 9f, BarGap = 3f, PillHeight = 44f, PillTop = 2f, PillFont = 17f, PillMaxWidth = 64f;
    /// <summary>A ribbon is this many screens wide; it is re-rendered when the
    /// playhead gets near either end.</summary>
    private const double BufferSpan = 3.0;
    /// <summary>Extra canvas before the window start, so a pill centred on the
    /// first moment of the window (the song start above all) is not cut by the
    /// canvas edge.</summary>
    private const float Lead = PillMaxWidth;

    private readonly AbsoluteLayout _layers, _leftClip, _rightClip;
    private readonly GraphicsView _pinned;
    private readonly BoxView _playhead;
    // [buffer] × {played, coming}: two windows, each in two colourings.
    private readonly GraphicsView[] _played = new GraphicsView[2], _coming = new GraphicsView[2];
    private readonly double[] _t0 = { double.NaN, double.NaN };
    private readonly double[] _drawnPlayed = { double.NaN, double.NaN }, _drawnComing = { double.NaN, double.NaN };
    private readonly List<(RectF Rect, double Start, int Index)>[] _pills = { new(), new() };   // ribbon coordinates, per buffer
    private int _active;
    private bool _pendingSwap, _pinnedShown;
    private int _pendingFrames;
    private readonly Dictionary<string, (float Width, string Top, string? Bottom)> _labels = new();
    private readonly Dictionary<int, string> _clocks = new();
    private float[]? _bars;
    private double _barSeconds;
    private double _tx;                                                     // translation of the visible ribbon
    private int _currentIndex = -1, _nextIndex = -1;
    /// <summary>The pill being played, on its own small canvas that rides along
    /// with the ribbon. The ribbons draw every pill in the resting style and
    /// never change when the chord does: re-rendering three screens of ribbon
    /// to recolour one pill was a visible hitch on every chord change on the
    /// phone (CoreText is slow with text), the one stutter left in the song.</summary>
    private readonly GraphicsView _current;
    /// <summary>Where each pill was actually drawn relative to its moment, once
    /// the no-overlap rule has nudged it: segment index → offset from x(start).</summary>
    private readonly Dictionary<int, float> _pillShift = new();
    private double _panStart, _panAt;
    private bool _panning;
    // A fling: the track keeps going after the finger lifts and slows to a stop,
    // as the beat view's scroll view does (user request 2026-09-14). Outside the
    // editor the scrub simply goes on until the coast ends; the song is sought
    // there and resumed only then.
    private readonly List<(long At, double Dx)> _swipe = new();
    private double _coastSpeed;                                  // points per second, in the finger's direction
    private long _coastTicks;
    private bool _coasting, _stoppedCoast;
    private const string CoastHandle = "coast";
    /// <summary>Slower than this, a lift is a stop, not a fling; and a coast ends.</summary>
    private const double FlingSpeed = 50, RestSpeed = 24;
    /// <summary>The stretch of the swipe the speed is read from, and how long
    /// after the last move a lift still counts as mid-swipe.</summary>
    private const long SwipeWindowMs = 160, LiftGraceMs = 100;
    private int _liftLogs;
    /// <summary>Seconds for the coast to lose two thirds of its speed.</summary>
    private const double CoastDecay = 0.45;
    /// <summary>Where the track is looking. Playing a song, that is wherever the
    /// song is; editing one, it is the reader's own and stays put while the song
    /// runs across it — a track that rode along with the playhead could not be
    /// worked on at all, and having to stop the song for every drag followed
    /// from that (user decision 2026-09-10). The playhead is paged back into
    /// view when it reaches an edge.</summary>
    private double _viewTime;
    private bool _viewSet, _following = true;

    /// <summary>The track is riding along with the song. It does so until the
    /// reader touches it — after that the window is theirs and the song runs on
    /// without it, until they ask to be taken back (user decision 2026-09-10).</summary>
    public bool Following
    {
        get => _following;
        private set { if (_following == value) return; _following = value; FollowingChanged?.Invoke(this, value); }
    }

    public event EventHandler<bool>? FollowingChanged;

    /// <summary>The window's first moment and how much of the song it shows —
    /// what the map over the track draws.</summary>
    public double ViewStart => ViewAt - Width * PlayheadAt / Math.Max(1, PixelsPerSecond);
    public double ViewSpan => Width / Math.Max(1, PixelsPerSecond);

    /// <summary>Put this moment in the middle of the window; the song is left where it is.</summary>
    public void LookAt(double middle)
    {
        Following = false;
        _viewSet = true;
        // The view is kept as the moment at the playhead's place, a quarter in —
        // not the middle. Taking the middle for it put what the map asked for a
        // quarter of a screen off (user report 2026-09-14).
        double pps = Math.Max(1, PixelsPerSecond);
        _viewTime = Math.Clamp(middle - Width * (0.5 - PlayheadAt) / pps, -Width * 0.5 / pps, Math.Max(0, Duration));
        Follow();
    }

    /// <summary>Back to the song, and along with it from now on.</summary>
    public void FollowNow()
    {
        _viewTime = Position;
        _viewSet = true;
        Following = true;
        Follow();
    }

    /// <summary>The reader has taken the window: stop riding along.</summary>
    private void Untether()
    {
        if (!Editing || !Following) return;
        _viewTime = ViewAt;
        _viewSet = true;
        Following = false;
    }
    private Color? _beatTick, _barTick, _playedBar;

    // ---- the A–B loop ------------------------------------------------------
    /// <summary>The top of the loop band: under the chord pills, over the wave.</summary>
    private const double LoopTop = PillTop + PillHeight + 4;
    /// <summary>The room a finger gets around a loop end.</summary>
    private const double HandleWidth = 44;
    /// <summary>Nothing shorter than this is worth a chord of its own.</summary>
    private const double MinClip = 0.15;
    private const int MostClips = 24;
    /// <summary>No loop shorter than this, whether tapped out or dragged.</summary>
    public const double MinLoopSeconds = 1.0;
    /// <summary>How near a chord a dragged end must come to take its moment —
    /// and, a little past it, to come away again. In points, so it feels the
    /// same wherever the song is.</summary>
    private const double SnapPoints = 16;
    /// <summary>The band is one box, moved and stretched by its transform: a
    /// canvas would have to be redrawn on every frame it grows, which is the
    /// one thing this control does not do.</summary>
    private readonly BoxView _loopBand, _cursor, _hole;
    /// <summary>The chord the playhead has just reached, ringed for a moment (see PlaceRing).</summary>
    private readonly Border _ring;
    /// <summary>The chord the ring is due on, the chord it is drawn on (the same,
    /// or the last one while it fades out), and the turn of its animation.</summary>
    private int _ringIndex = -1, _ringShown = -1, _ringTurn;
    /// <summary>How long of the song a reached chord keeps its ring.</summary>
    private const double RingSeconds = 0.5;
    private readonly Border _cursorMark;
    private readonly BoxView _lineA, _lineB;
    private readonly Border _gripA, _gripB;
    private readonly List<BoxView> _gripBars = new();
    private readonly View _handleA, _handleB;
    private double _handleAx, _handleBx;
    private int _dragging;                                       // 0 none, 1 the start, 2 the end
    private double _dragFrom, _dragPressX, _dragDx, _scrolled, _posAtPress, _snapped = double.NaN;
    private long _edgeSince;
    private IDispatcherTimer? _scroller;

    public ChordTrack()
    {
        _layers = new AbsoluteLayout { IsClippedToBounds = true };
        _leftClip = new AbsoluteLayout { IsClippedToBounds = true, InputTransparent = true };
        _rightClip = new AbsoluteLayout { IsClippedToBounds = true, InputTransparent = true };
        for (int i = 0; i < 2; i++)
        {
            _played[i] = new GraphicsView { Drawable = new RibbonDrawable(this, i, played: true), Opacity = i == 0 ? 1 : 0, InputTransparent = true };
            _coming[i] = new GraphicsView { Drawable = new RibbonDrawable(this, i, played: false), Opacity = i == 0 ? 1 : 0, InputTransparent = true };
            _leftClip.Add(_played[i]);
            _rightClip.Add(_coming[i]);
        }
        // Over the loop band, not under it: the playhead is the one thing on
        // the track that must never be tinted by anything.
        _playhead = new BoxView { CornerRadius = 2, InputTransparent = true, WidthRequest = 4, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        _pinned = new GraphicsView { Drawable = new PinnedDrawable(this), IsVisible = false };
        _current = new GraphicsView { Drawable = new CurrentDrawable(this), IsVisible = false, InputTransparent = true };
        _layers.Add(_leftClip);
        _layers.Add(_rightClip);
        _layers.Add(_current);
        _layers.Add(_pinned);
        Add(_layers);
        // What a chord taken hold of leaves behind: its old place, covered in the
        // track's own ground, so nothing is redrawn for it (user request 2026-09-14).
        _hole = new BoxView
        {
            CornerRadius = 14, IsVisible = false, InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            HeightRequest = PillHeight + 2, Margin = new Thickness(0, PillTop - 1, 0, 0),
        };
        Add(_hole);
        _ring = new Border
        {
            StrokeThickness = 2.5, BackgroundColor = Colors.Transparent, Padding = 0, IsVisible = false, InputTransparent = true,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(13) },
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            HeightRequest = PillHeight, Margin = new Thickness(0, PillTop, 0, 0),
        };
        Add(_ring);
        // The editor's cursor: the middle of the window, where an added chord
        // lands. It stands still while the track moves under it.
        _cursor = new BoxView { WidthRequest = 2, InputTransparent = true, IsVisible = false, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        _cursorMark = new Border
        {
            WidthRequest = Metrics.Instance.Size(22), HeightRequest = Metrics.Instance.Size(22), Padding = 0, StrokeThickness = 0, InputTransparent = true, IsVisible = false,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(Metrics.Instance.Size(22) / 2) },
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            Content = new IconView { Name = "plus", Size = Metrics.Instance.Size(14), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        _loopBand = new BoxView
        {
            InputTransparent = true, IsVisible = false,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        };
        Add(_loopBand);
        Add(_cursor);
        Add(_cursorMark);
        Add(_playhead);
        var overlay = new BoxView { Color = Colors.Transparent };
        Add(overlay);
        PointerDrag.Attach(overlay, new PointerDrag.Callbacks
        {
            Started = _ =>
            {
                if (_dragging != 0) return;                      // a loop end has the finger
                // A finger on a coasting track stops it; the scrub it was is not over.
                bool wasCoasting = _coasting;
                if (wasCoasting) StopCoast();
                _stoppedCoast = wasCoasting;
                _panning = true;
                _swipe.Clear();
                Untether();
                _panStart = _panAt = ViewAt;
                // Editing, the finger moves the track and not the song, so the
                // song is left playing.
                if (!Editing && !wasCoasting) ScrubStarted?.Invoke(this, EventArgs.Empty);
            },
            Moved = dx =>
            {
                if (!_panning) return;
                long now = Environment.TickCount64;
                _swipe.Add((now, dx));
                _swipe.RemoveAll(sample => now - sample.At > SwipeWindowMs);
                Pan(Math.Clamp(_panStart - dx / PixelsPerSecond, Editing ? Lowest : 0, Math.Max(0, Duration)));
            },
            Ended = () =>
            {
                if (!_panning) return;
                _panning = false;
                double speed = SwipeSpeed();
                bool fling = Math.Abs(speed) >= FlingSpeed;
                if (_liftLogs++ < 12)
                    Strunika.Core.Diagnostics.FileLog.Info($"conveyor lift: {_swipe.Count} samples over {(_swipe.Count > 0 ? _swipe[^1].At - _swipe[0].At : 0)} ms, last {(_swipe.Count > 0 ? Environment.TickCount64 - _swipe[^1].At : -1)} ms ago, {speed:0} pt/s → {(fling ? "coast" : "stop")}");
                if (fling) { StartCoast(speed); return; }
                if (!Editing) ScrubEnded?.Invoke(this, _panAt);
            },
            Tapped = pt =>
            {
                if (_dragging != 0) return;
                // A press that stopped a coast is only that: the scrub ends where
                // the track came to rest. Any other press that did not move: the
                // scrub ends where it began, then the tap seeks.
                if (_stoppedCoast) { _stoppedCoast = false; _panning = false; if (!Editing) ScrubEnded?.Invoke(this, _panAt); return; }
                if (_panning) { _panning = false; if (!Editing) ScrubEnded?.Invoke(this, _panAt); }
                TapAt(pt);
            },
        });
        // The ends go on last, over the scrub layer: a touch lands on the
        // topmost view, so taking hold of an end never scrubs the song.
        _handleA = BuildHandle(out _lineA, out _gripA);
        _handleB = BuildHandle(out _lineB, out _gripB);
        Add(_handleA);
        Add(_handleB);
        AttachHandle(_handleA, 1);
        AttachHandle(_handleB, 2);
        IsClippedToBounds = true;                                // an end dragged off the track stays off it
        ApplyColours();
        SizeChanged += (_, _) => Relayout();
        Loaded += (_, _) => { ResetBuffers(); Follow(); };             // canvases exist now: render the first window
    }

    /// <summary>Re-render every ribbon and the pinned pill (data or colours changed).</summary>
    /// <summary>The moment at the playhead's place on screen.</summary>
    private double ViewAt => Editing ? _viewTime : Position;

    /// <summary>The track is still moving after a fling.</summary>
    public bool Coasting => _coasting;

    /// <summary>The finger's or the coast's new place: the window in the editor,
    /// the song outside it. Never assign Position here — the owner applies it
    /// and it comes back.</summary>
    private void Pan(double at)
    {
        _panAt = at;
        if (Editing) { _viewTime = at; Follow(); return; }
        Scrubbing?.Invoke(this, at);
    }

    /// <summary>Points per second over the last stretch of the swipe, in the
    /// finger's direction; nothing when the finger had already paused before
    /// it lifted.</summary>
    private double SwipeSpeed()
    {
        if (_swipe.Count < 2) return 0;
        var last = _swipe[^1];
        if (Environment.TickCount64 - last.At > LiftGraceMs) return 0;   // held still, then lifted
        var first = _swipe[0];
        double seconds = (last.At - first.At) / 1000.0;
        if (seconds < 0.015) return 0;
        return (last.Dx - first.Dx) / seconds;
    }

    /// <summary>How far before the song's start the window may look: as far as
    /// LookAt allows, so a pan or a coast near the start is not thrown to zero
    /// and stopped there (that ended every fling begun there at once).</summary>
    private double Lowest => -Width * 0.5 / Math.Max(1, PixelsPerSecond);

    private void StartCoast(double speed)
    {
        _coastSpeed = speed;
        _coastTicks = Environment.TickCount64;
        _coasting = true;
        this.AbortAnimation(CoastHandle);
        new Animation(_ => Coast()).Commit(this, CoastHandle, length: 60_000, repeat: () => _coasting);
    }

    /// <summary>A frame of the coast: on in the finger's direction, a little
    /// slower each frame, until it rests or reaches an end of the song.</summary>
    private void Coast()
    {
        if (!_coasting) return;
        long now = Environment.TickCount64;
        double dt = Math.Min(0.05, (now - _coastTicks) / 1000.0);
        _coastTicks = now;
        double duration = Math.Max(0, Duration), lowest = Editing ? Lowest : 0;
        double at = Math.Clamp(_panAt - _coastSpeed * dt / Math.Max(1, PixelsPerSecond), lowest, duration);
        _coastSpeed *= Math.Exp(-dt / CoastDecay);
        Pan(at);
        if (Math.Abs(_coastSpeed) < RestSpeed || at <= lowest || at >= duration) Settle();
    }

    private void StopCoast()
    {
        if (!_coasting) return;
        _coasting = false;
        this.AbortAnimation(CoastHandle);
    }

    /// <summary>The coast is over, here: outside the editor the scrub ends and the
    /// song is sought (and resumed, if it was playing). The owner calls this
    /// before playing so the track is still first (user rule 2026-09-14).</summary>
    public void Settle()
    {
        if (!_coasting) return;
        StopCoast();
        if (!Editing) ScrubEnded?.Invoke(this, _panAt);
    }

    /// <summary>The moment in the middle of the window: where a chord is put
    /// when one is added, and where the editor's cursor stands.</summary>
    public double CentreTime => ViewAt + Width * (0.5 - PlayheadAt) / Math.Max(1, PixelsPerSecond);

    public void Redraw()
    {
        _pillShift.Clear();
        for (int i = 0; i < 2; i++) { _played[i].Invalidate(); _coming[i].Invalidate(); }
        _pinned.Invalidate();
        _current.Invalidate();
    }

    private void ResetBuffers()
    {
        for (int i = 0; i < 2; i++) _t0[i] = _drawnPlayed[i] = _drawnComing[i] = double.NaN;
        _pendingSwap = false;
    }

    private void ApplyColours()
    {
        _playhead.Color = Accent;
        _loopBand.Color = Accent.WithAlpha(0.14f);
        _cursor.Color = TextColor.WithAlpha(0.35f);
        _cursorMark.BackgroundColor = Accent;
        _hole.Color = BackdropColor;
        _ring.Stroke = Accent;
        if (_cursorMark.Content is IconView mark) mark.Color = OnAccent;
        _lineA.Color = _lineB.Color = Accent;
        _gripA.BackgroundColor = _gripB.BackgroundColor = Accent;
        foreach (var bar in _gripBars) bar.Color = OnAccent;
        _playedBar = null;
        Redraw();
    }

    private double _laidOutW, _laidOutH;

    private void Relayout()
    {
        double w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        if (Math.Abs(w - _laidOutW) < 0.01 && Math.Abs(h - _laidOutH) < 0.01) return;   // the same size again: nothing to lay out
        _laidOutW = w;
        _laidOutH = h;
        double px = w * PlayheadAt, bw = w * BufferSpan + Lead;
        // Both halves are the track's width and are moved to meet at the playhead
        // (FollowCore), so the meeting point can follow a playhead that moves.
        AbsoluteLayout.SetLayoutBounds(_leftClip, new Rect(0, 0, w, h));
        AbsoluteLayout.SetLayoutBounds(_rightClip, new Rect(0, 0, w, h));
        for (int i = 0; i < 2; i++)
        {
            AbsoluteLayout.SetLayoutBounds(_played[i], new Rect(0, 0, bw, h));
            AbsoluteLayout.SetLayoutBounds(_coming[i], new Rect(0, 0, bw, h));
        }
        AbsoluteLayout.SetLayoutBounds(_pinned, new Rect(w - PillMaxWidth - 14, 0, PillMaxWidth + 14, PillTop + PillHeight + 2));
        AbsoluteLayout.SetLayoutBounds(_current, new Rect(0, 0, PillMaxWidth + 12, PillTop + PillHeight + 2));
        // The band is as wide as the track and scaled down to the loop, so the
        // scale stays between 0 and 1 whatever the zoom.
        double band = Math.Max(0, h - LoopTop);
        _playhead.HeightRequest = Math.Max(0, band - 2);
        _playhead.Margin = new Thickness(0, LoopTop, 0, 0);
        // (Its place across the track is Follow's to set — in the editor it is
        // not at px, and parking it there first made it jump.)
        _cursor.HeightRequest = Math.Max(0, h - PillTop - 6);
        _cursor.Margin = new Thickness(0, PillTop, 0, 0);
        double mark = Metrics.Instance.Size(22);
        _cursorMark.Margin = new Thickness(0, Math.Max(0, h - mark - 4), 0, 0);
        NativeTransform.TranslateX(_cursor, w / 2 - 1);
        NativeTransform.TranslateX(_cursorMark, w / 2 - mark / 2);
        _loopBand.WidthRequest = w;
        _loopBand.HeightRequest = band;
        _loopBand.Margin = new Thickness(0, LoopTop, 0, 0);
        foreach (var handle in new[] { _handleA, _handleB })
        {
            handle.HeightRequest = band;
            handle.Margin = new Thickness(0, LoopTop, 0, 0);
        }
        _bars = null;
        ResetBuffers();
        Follow();
    }

    // ---- following the song --------------------------------------------

    /// <summary>Called on every position change: slide the ribbons, re-render
    /// only when the playhead nears the window edge or the current chord changes.</summary>
    private void Follow()
    {
        try { FollowCore(); }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }

    private void FollowCore()
    {
        double w = Width;
        if (w <= 0) return;
        double pps = PixelsPerSecond, v = w / pps, px = w * PlayheadAt;
        if (!Editing)
        {
            _viewSet = false;
            _following = true;
            // At its own place. Relayout used to put it there and no longer does
            // (in the editor that made it jump), so a song opened fresh had the
            // playhead at the edge and one out of the editor had none (user
            // report 2026-09-14).
            NativeTransform.TranslateX(_playhead, px - 2);
        }
        else
        {
            // Riding along, the window is the song's; let go of, it is the
            // reader's and stays exactly where they left it — the song simply
            // runs out of the frame, and the map over the track says where it
            // has gone.
            if (!_viewSet || Following) { _viewTime = Position; _viewSet = true; }
            NativeTransform.TranslateX(_playhead, px + (Position - _viewTime) * pps - 2);
        }
        double pos = ViewAt;
        int back = 1 - _active;
        double t0a = _t0[_active];
        bool farOutside = !double.IsNaN(t0a) && (pos < t0a - 0.25 * v || pos > t0a + (BufferSpan + 0.25) * v);
        if (double.IsNaN(t0a) || farOutside)
        {
            // First window, or a jump so far that the visible buffer shows nothing
            // anyway (a drag across the song): render straight into it — there is
            // no smooth content to protect and waiting for the spare only delays.
            _t0[_active] = Math.Max(0, pos - 1.25 * v);
            _drawnPlayed[_active] = _drawnComing[_active] = double.NaN;
            Invalidate(_active);
            _pendingSwap = false;
        }
        else if (pos < t0a + 0.35 * v || pos > t0a + (BufferSpan - 0.85) * v)
        {
            // Nearing the edge: render the next window into the spare buffer.
            // While a swap is pending and the position has left that window
            // too (a fast drag), re-aim the spare.
            double want = Math.Max(0, pos - 1.25 * v);
            if (!_pendingSwap || pos < _t0[back] + 0.35 * v || pos > _t0[back] + (BufferSpan - 0.85) * v)
            {
                _t0[back] = want;
                _drawnPlayed[back] = _drawnComing[back] = double.NaN;
                Invalidate(back);
                _pendingSwap = true;
                _pendingFrames = 0;
            }
        }
        if (_pendingSwap)
        {
            _pendingFrames++;
            if (_drawnPlayed[back] == _t0[back] && _drawnComing[back] == _t0[back])
            {
                // Both colourings of the spare are on their canvases: swap.
                _played[back].Opacity = _coming[back].Opacity = 1;
                _played[_active].Opacity = _coming[_active].Opacity = 0;
                _active = back;
                back = 1 - _active;
                _pendingSwap = false;
                if (_pendingFrames > 4) Strunika.Core.Diagnostics.FileLog.Info($"conveyor: spare buffer took {_pendingFrames} frames to draw");
            }
        }
        // Played and coming meet at the playhead's fixed place. (For a day the
        // editor moved this with the playhead so the chords it had passed could
        // take a colour of their own; the user dropped that, 2026-09-14.)
        double boundary = px;
        NativeTransform.TranslateX(_leftClip, boundary - w);
        NativeTransform.TranslateX(_rightClip, boundary);
        for (int i = 0; i < 2; i++)
        {
            if (double.IsNaN(_t0[i])) continue;
            double tx = px - (pos - _t0[i]) * pps - Lead;
            NativeTransform.TranslateX(_played[i], tx - (boundary - w));
            NativeTransform.TranslateX(_coming[i], tx - boundary);
        }
        _tx = px - (pos - _t0[_active]) * pps - Lead;

        var segments = Segments;
        int current = IndexAt(Position), next = NextAfter(Position);
        if (current != _currentIndex)
        {
            // Only the small canvas changes; the ribbons stay as they are.
            _currentIndex = current;
            bool show = current >= 0 && segments != null && current < segments.Count && segments[current].Label != "—";
            if (_current.IsVisible != show) _current.IsVisible = show;
            if (show) _current.Invalidate();
        }
        if (_current.IsVisible && segments != null && _currentIndex >= 0 && _currentIndex < segments.Count)
        {
            // Over the very pill the ribbon drew, wherever the no-overlap rule put it.
            var seg = segments[_currentIndex];
            float pillW = _labels.TryGetValue(seg.Label, out var cm) ? cm.Width : 46f;
            float shift = _pillShift.TryGetValue(_currentIndex, out var sh) ? sh : -pillW / 2;
            NativeTransform.TranslateX(_current, px + (seg.Start - ViewAt) * pps + shift);
        }
        UpdateLoop();
        PlaceClips();
        if (_cursor.IsVisible != Editing) _cursor.IsVisible = _cursorMark.IsVisible = Editing;
        if (Editing)
        {
            if (_current.IsVisible) _current.IsVisible = false;   // the blocks say it better
            if (_pinned.IsVisible) { _pinned.IsVisible = false; _pinnedShown = false; }
            PlaceRing(segments, px, pos, pps);
            return;
        }
        if (_ringIndex >= 0 || _ringShown >= 0) { _ringIndex = -1; HideRingNow(); }
        if (next != _nextIndex) { _nextIndex = next; _pinnedShown = false; _pinned.Invalidate(); }
        if (next >= 0 && segments != null)
        {
            // The preview is latched, not recomputed: it only *starts* while the
            // real pill is still a screen away, and once shown it waits in its slot
            // until the real one slides down to exactly that spot. Deciding afresh
            // every frame made it blink into view just as the real pill arrived.
            float pillW = _labels.TryGetValue(segments[next].Label, out var m) ? m.Width : 46f;
            float realLeft = (float)(px + (segments[next].Start - pos) * pps) - pillW / 2;
            float slotLeft = (float)w - pillW - 2f;
            if (!_pinnedShown) { if (realLeft > (float)w + pillW) _pinnedShown = true; }
            else if (realLeft <= slotLeft) _pinnedShown = false;
        }
        else
        {
            _pinnedShown = false;
        }
        if (_pinned.IsVisible != _pinnedShown) _pinned.IsVisible = _pinnedShown;
    }

    /// <summary>
    /// In the editor, the chord the playhead reaches wears a ring in the accent:
    /// from the moment the playhead crosses the middle of its badge, for half a
    /// second of the song or until the next badge's middle is reached, whichever
    /// comes first — and never on the chosen chord, which is marked already (user
    /// request 2026-09-14). The ring is a box moved by its transform; it is only
    /// resized when another chord takes it.
    /// </summary>
    private void PlaceRing(IReadOnlyList<ChordSegmentDto>? segments, double px, double pos, double pps)
    {
        int index = RingAt(segments, pps);
        if (index != _ringIndex)
        {
            _ringIndex = index;
            if (index >= 0) { _ringShown = index; ShowRing(PillWidth(segments![index].Label)); }
            else HideRing();
        }
        // Drawn on its chord to the last, fading included: a ring left where it
        // was while the track scrolled or the chord left the screen stayed
        // behind on its own (user report 2026-09-14).
        int shown = _ringShown;
        if (shown < 0 || !_ring.IsVisible || segments == null || shown >= segments.Count) return;
        var segment = segments[shown];
        double width = PillWidth(segment.Label);
        double shift = _pillShift.TryGetValue(shown, out var nudge) ? nudge : -width / 2;
        double x = px + (segment.Start - pos) * pps + shift;
        if (x + width < 0 || x > Width) { HideRingNow(); return; }
        NativeTransform.TranslateX(_ring, x);
    }

    private void HideRingNow()
    {
        ++_ringTurn;
        _ringShown = -1;
        _ring.CancelAnimations();
        _ring.IsVisible = false;
        _ring.Opacity = 1;
    }

    /// <summary>The chord whose ring is due at the playhead, or −1.</summary>
    private int RingAt(IReadOnlyList<ChordSegmentDto>? segments, double pps)
    {
        if (segments == null || segments.Count == 0) return -1;
        double position = Position;
        int last = -1;
        double lastMiddle = 0, nextMiddle = double.PositiveInfinity;
        // The last badge whose middle the playhead has passed — this chord's, or
        // the one before when the no-overlap rule pushed this one along — and the
        // next badge's middle.
        for (int i = Math.Max(0, IndexAt(position) - 1); i < segments.Count; i++)
        {
            if (segments[i].Label == "—") continue;
            double width = PillWidth(segments[i].Label);
            double shift = _pillShift.TryGetValue(i, out var nudge) ? nudge : -width / 2;
            double middle = segments[i].Start + (shift + width / 2) / pps;
            if (middle <= position) { last = i; lastMiddle = middle; }
            else { nextMiddle = middle; break; }
        }
        if (last < 0 || last == Selected) return -1;
        return position < lastMiddle + RingSeconds && position < nextMiddle ? last : -1;
    }

    private double PillWidth(string label) => _labels.TryGetValue(label, out var measured) ? measured.Width : 46;

    private void ShowRing(double width)
    {
        ++_ringTurn;
        _ring.CancelAnimations();
        _ring.Opacity = 1;
        if (Math.Abs(_ring.WidthRequest - width) > 0.5) _ring.WidthRequest = width;
        _ring.IsVisible = true;
    }

    private async void HideRing()
    {
        if (!_ring.IsVisible) { _ringShown = -1; return; }
        int turn = ++_ringTurn;
        if (!Services.Motion.Reduced) await _ring.FadeToAsync(0, 140, Easing.CubicOut);
        if (turn != _ringTurn) return;                           // another chord took the ring meanwhile
        _ring.IsVisible = false;
        _ring.Opacity = 1;
        _ringShown = -1;
    }

    private void Invalidate(int buffer)
    {
        try
        {
            _played[buffer].Invalidate();
            _coming[buffer].Invalidate();
        }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }

    private int IndexAt(double pos)
    {
        var segs = Segments;
        if (segs == null) return -1;
        if (_currentIndex >= 0 && _currentIndex < segs.Count && pos >= segs[_currentIndex].Start && pos < segs[_currentIndex].End) return _currentIndex;
        for (int i = 0; i < segs.Count; i++)
            if (pos >= segs[i].Start && pos < segs[i].End) return i;
        return -1;
    }

    private int NextAfter(double pos)
    {
        var segs = Segments;
        if (segs == null) return -1;
        for (int i = 0; i < segs.Count; i++)
            if (segs[i].Start > pos && segs[i].Label != "—") return i;
        return -1;
    }

    // ---- gestures ---------------------------------------------------------

    // ---- the A–B loop ------------------------------------------------------

    /// <summary>An end of the loop: a line down the band with a grip on it, and
    /// 44 pt of clear room around it for a finger. The grip is what grows when
    /// it is held — the host carries the transform that follows the song, and
    /// two transforms on one view fight each other on Windows.</summary>
    private View BuildHandle(out BoxView line, out Border grip)
    {
        line = new BoxView { WidthRequest = 3, CornerRadius = 1.5, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Fill, InputTransparent = true };
        var bars = new HorizontalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        for (int i = 0; i < 2; i++)
        {
            var bar = new BoxView { WidthRequest = 2, HeightRequest = Metrics.Instance.Size(14), CornerRadius = 1, VerticalOptions = LayoutOptions.Center };
            _gripBars.Add(bar);
            bars.Add(bar);
        }
        grip = new Border
        {
            WidthRequest = Metrics.Instance.Size(18), HeightRequest = Metrics.Instance.Size(40), Padding = 0, StrokeThickness = 0, InputTransparent = true,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(Metrics.Instance.Size(18) / 2) },
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Content = bars,
        };
        grip.Shadow = new Shadow { Brush = Brush.Black, Radius = 6, Opacity = 0.35f, Offset = new Point(0, 2) };
        return new Grid
        {
            WidthRequest = HandleWidth, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            IsVisible = false, Opacity = 0,
            Children = { new BoxView { Color = Colors.Transparent }, line, grip },
        };
    }

    private void AttachHandle(View handle, int which) =>
        PointerDrag.Attach(handle, new PointerDrag.Callbacks
        {
            Started = _ => BeginHandle(which),
            Moved = dx => { if (_dragging == which) { _dragDx = dx; ApplyHandle(); } },
            Ended = EndHandle,
            Tapped = _ => EndHandle(),
        });

    private Border Grip(int which) => which == 1 ? _gripA : _gripB;

    /// <summary>Where the loop is on screen, every frame, with nothing redrawn:
    /// the band is translated and stretched, the ends are translated.</summary>
    private void UpdateLoop()
    {
        try
        {
            double w = Width;
            if (w <= 0) return;
            double a = LoopStart, b = LoopEnd, pps = PixelsPerSecond, px = w * PlayheadAt, pos = ViewAt;
            bool taking = a >= 0 && b <= a && LoopArmed;         // the end is still to come
            bool whole = a >= 0 && b > a;
            bool show = taking || whole;
            if (_loopBand.IsVisible != show) _loopBand.IsVisible = show;
            if (_handleA.IsVisible != whole) ShowHandles(whole);
            if (!show) return;

            double left = px + (a - pos) * pps;
            double right = taking ? px : px + (b - pos) * pps;
            // Kept to the track: a loop half an hour away would otherwise ask
            // for a transform of a hundred thousand points.
            double from = Math.Clamp(left, -8, w + 8), to = Math.Clamp(right, -8, w + 8);
            NativeTransform.TranslateX(_loopBand, from);
            NativeTransform.ScaleX(_loopBand, Math.Max(0, to - from) / Math.Max(1, w));
            if (!whole) return;
            _handleAx = left - HandleWidth / 2;
            _handleBx = right - HandleWidth / 2;
            NativeTransform.TranslateX(_handleA, _handleAx);
            NativeTransform.TranslateX(_handleB, _handleBx);
        }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }

    private void ShowHandles(bool show)
    {
        foreach (var handle in new[] { _handleA, _handleB })
        {
            if (!show) { handle.IsVisible = false; handle.Opacity = 0; continue; }
            handle.IsVisible = true;
            if (Services.Motion.Reduced) { handle.Opacity = 1; continue; }
            handle.Opacity = 0;
            _ = handle.FadeToAsync(1, 180, Easing.CubicOut);
        }
        if (!show) return;
        foreach (var grip in new[] { _gripA, _gripB })
        {
            if (Services.Motion.Reduced) { grip.Scale = 1; continue; }
            grip.Scale = 0.6;
            _ = grip.ScaleToAsync(1, 260, Easing.SpringOut);
        }
    }

    private void BeginHandle(int which)
    {
        if (LoopStart < 0 || LoopEnd <= LoopStart) return;
        _dragging = which;
        _dragFrom = which == 1 ? LoopStart : LoopEnd;
        _dragDx = 0;
        _scrolled = 0;
        _snapped = double.NaN;
        _edgeSince = 0;
        _posAtPress = Position;
        // The finger is on the grip, which is the middle of the handle: that is
        // the point the edge-scrolling watches.
        _dragPressX = (which == 1 ? _handleAx : _handleBx) + HandleWidth / 2;
        if (!Services.Motion.Reduced) _ = Grip(which).ScaleToAsync(1.3, 120, Easing.CubicOut);
        LoopEditStarted?.Invoke(this, EventArgs.Empty);
        (_scroller ??= NewScroller()).Start();
    }

    private void ApplyHandle()
    {
        double a = LoopStart, b = LoopEnd, duration = Math.Max(0, Duration);
        if (_dragging == 0 || a < 0 || b <= a) return;           // the loop went away under the finger
        double t = Snap(_dragFrom + _dragDx / PixelsPerSecond + _scrolled, end: _dragging == 2);
        // The ends never cross and never come closer than a loop worth playing.
        if (_dragging == 1) t = Math.Clamp(t, 0, Math.Max(0, b - MinLoopSeconds));
        else t = Math.Clamp(t, Math.Min(a + MinLoopSeconds, duration), duration);
        LoopChanging?.Invoke(this, _dragging == 1 ? (t, b) : (a, t));
    }

    /// <summary>The moment of the nearest chord when one is within reach, the
    /// time under the finger when none is. A buzz marks the moment it takes
    /// hold, so a snap can be felt without looking.</summary>
    private double Snap(double t, bool end)
    {
        double window = SnapPoints / PixelsPerSecond, best = t, nearest = window;
        if (Segments is { } segments)
            foreach (var segment in segments)
            {
                double d = Math.Abs(segment.Start - t);
                if (d < nearest) { nearest = d; best = segment.Start; }
            }
        if (end && Duration > 0 && Math.Abs(Duration - t) < nearest) { nearest = Math.Abs(Duration - t); best = Duration; }
        bool took = nearest < window;
        double now = took ? best : double.NaN;
        if (took && (double.IsNaN(_snapped) || Math.Abs(now - _snapped) > 1e-6))
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { /* no engine */ }
        _snapped = now;
        return best;
    }

    private IDispatcherTimer NewScroller()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += (_, _) => Scroll();
        return timer;
    }

    /// <summary>An end held against the edge of the track pulls the song along,
    /// so a loop can reach past what is on screen.</summary>
    private void Scroll()
    {
        double w = Width;
        if (_dragging == 0 || w <= 0) return;
        const double Zone = 64, Fastest = 280;                   // points, points per second
        const double Patience = 3, Impatient = 4;                // seconds, then up to this many times as fast
        double x = _dragPressX + _dragDx;
        double speed = x < Zone ? -Fastest * Math.Min(1, (Zone - x) / Zone)
                     : x > w - Zone ? Fastest * Math.Min(1, (x - (w - Zone)) / Zone)
                     : 0;
        if (speed == 0) { _edgeSince = 0; return; }
        // Held at the edge, the song moves at its own pace for three seconds and
        // then loses patience, climbing to four times that over the three after
        // it: a loop end can be carried the length of a song without lifting the
        // finger, and a short move is still exact (user request 2026-09-09).
        if (_edgeSince == 0) _edgeSince = Environment.TickCount64;
        double held = (Environment.TickCount64 - _edgeSince) / 1000.0;
        if (held > Patience) speed *= Math.Min(Impatient, 1 + (held - Patience));
        double at = _posAtPress + _scrolled;
        double next = Math.Clamp(at + speed * 0.05 / PixelsPerSecond, 0, Math.Max(0, Duration));
        if (Math.Abs(next - at) < 1e-6) return;                  // the song has no more to give that way
        _scrolled += next - at;
        Scrubbing?.Invoke(this, next);
        ApplyHandle();
    }

    private void EndHandle()
    {
        if (_dragging == 0) return;
        var grip = Grip(_dragging);
        _dragging = 0;
        _scroller?.Stop();
        _snapped = double.NaN;
        _edgeSince = 0;
        if (!Services.Motion.Reduced) _ = grip.ScaleToAsync(1, 220, Easing.SpringOut);
        LoopEditEnded?.Invoke(this, Position);
    }

    // ---- the editor's blocks ------------------------------------------------

    /// <summary>What the finger takes hold of: the chord's own badge, where the
    /// ribbon drew it. A badge is where a chord begins, so dragging one moves
    /// that beginning — and with it the end of the chord before, since the song
    /// has no gaps. The end of a chord is the next badge, and is moved there.
    /// <para>The badge is a view rather than a hit test because a pan on iOS
    /// never says where it began; and while it is dragged it is its own ghost,
    /// moved by its transform, so nothing is redrawn until it is let go.</para>
    /// </summary>
    private sealed class Block
    {
        public required Grid Host;
        public required BoxView Body;
        /// <summary>The chord on the finger and in its pulse, drawn by the track's
        /// own pill code in its own chosen colours — a label in a box of the
        /// measured width cut the name to "…" (user report 2026-09-14).</summary>
        public required GraphicsView Face;
        /// <summary>Dotted handles on the chord being worked on.</summary>
        public required GraphicsView Handles;
        public int Index = -1;
        public int PulseTurn;
    }

    private readonly List<Block> _clips = new();
    private bool _clipDragging, _clipMoved;
    private int _clipIndex = -1;
    private double _clipFrom, _clipTo, _clipDx, _clipStart;
    /// <summary>The chord off the track: from its first move until the song's
    /// chords come back with the move in them.</summary>
    private Block? _lifted;

    private Block NewClip()
    {
        var face = new GraphicsView { IsVisible = false, InputTransparent = true, BackgroundColor = Colors.Transparent };
        var handles = new GraphicsView { Drawable = new HandlesDrawable(this), IsVisible = false, InputTransparent = true, BackgroundColor = Colors.Transparent };
        var body = new BoxView { Color = Colors.Transparent };
        var host = new Grid
        {
            HeightRequest = PillTop + PillHeight + 4,
            VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.Start,
            IsVisible = false,
            Children = { body, face, handles },
        };
        var clip = new Block { Host = host, Body = body, Face = face, Handles = handles };
        face.Drawable = new LiftDrawable(this, clip);
        PointerDrag.Attach(body, new PointerDrag.Callbacks
        {
            Started = _ => BeginClip(clip),
            Moved = dx => { if (_clipDragging && _clipIndex == clip.Index) { _clipDx = dx; DragClip(clip); } },
            Ended = () => EndClip(clip),
            // A press that did not move is a choice, not a move; so is a finger
            // simply held there (user request 2026-09-14). Either way it pulses.
            Tapped = _ => { _clipDragging = false; SelectionRequested?.Invoke(this, clip.Index); Pulse(clip); },
            Held = _ =>
            {
                if (_lifted != null) return;
                Services.Haptics.Default.Success();              // the buzz marks the hold, not the drag (user rule 2026-09-14)
                SelectionRequested?.Invoke(this, clip.Index);
                Pulse(clip);
            },
        });
        Add(host);
        _clips.Add(clip);
        return clip;
    }

    private void BeginClip(Block clip)
    {
        var segments = Segments;
        if (!Editing || _lifted != null || segments == null || clip.Index < 0 || clip.Index >= segments.Count) return;
        Untether();
        _clipDragging = true;
        _clipIndex = clip.Index;
        _clipDx = 0;
        _clipMoved = false;
        _clipFrom = _clipStart = segments[clip.Index].Start;
        _clipTo = segments[clip.Index].End;
    }

    private void DragClip(Block clip)
    {
        if (!_clipMoved)
        {
            if (Math.Abs(_clipDx) < 3) return;                   // a finger settling, not a move
            _clipMoved = true;
            SelectionRequested?.Invoke(this, clip.Index);
            Lift(clip);
        }
        // To the nearest sixteenth, and without a buzz (user rule 2026-09-14).
        double moved = BeatMath.Snap(Beats ?? Array.Empty<double>(), _clipFrom + _clipDx / PixelsPerSecond);
        // Anywhere in the song: the neighbours are no walls — the song puts the
        // chord down wherever it lands and whatever is there gives way (user
        // report 2026-09-14).
        _clipStart = Math.Clamp(moved, 0, Math.Max(0, Duration - MinClip));
        PlaceClip(clip, _clipStart);
    }

    /// <summary>The chord comes away from its place: the place left empty, and
    /// the chord itself on the finger in its own colours, with a pulse. The buzz
    /// came with the hold before it (user request 2026-09-14).</summary>
    private void Lift(Block clip)
    {
        var segments = Segments;
        if (segments == null || clip.Index < 0 || clip.Index >= segments.Count) return;
        _lifted = clip;
        double width = _labels.TryGetValue(segments[clip.Index].Label, out var measured) ? measured.Width : 46;
        double shift = _pillShift.TryGetValue(clip.Index, out var nudge) ? nudge : -width / 2;
        _hole.WidthRequest = width + 2;
        NativeTransform.TranslateX(_hole, Width * PlayheadAt + (segments[clip.Index].Start - ViewAt) * PixelsPerSecond + shift - 1);
        _hole.IsVisible = true;
        clip.Handles.IsVisible = false;
        clip.Face.IsVisible = true;
        clip.Face.Invalidate();
        Pulse(clip);
    }

    /// <summary>The chord grows a little and settles: the sign it was chosen,
    /// with no change of colour (user request 2026-09-14). Its own pill is laid
    /// over the ribbon's for the moment.</summary>
    private async void Pulse(Block clip)
    {
        int turn = ++clip.PulseTurn;
        clip.Face.IsVisible = true;
        clip.Face.Invalidate();
        if (!Services.Motion.Reduced)
        {
            await clip.Face.ScaleToAsync(1.12, 110, Easing.CubicOut);
            await clip.Face.ScaleToAsync(1.0, 220, Easing.SpringOut);
        }
        if (turn != clip.PulseTurn || clip == _lifted) return;  // pulsed again, or on the finger now
        clip.Face.IsVisible = false;
    }

    private void EndClip(Block clip)
    {
        if (!_clipDragging) return;
        _clipDragging = false;
        if (!_clipMoved) return;
        _clipMoved = false;
        // The chord stays lifted where it was let go, and its old place empty,
        // until the song's chords come back with the move in them: putting
        // either down first showed the old badge again for a frame.
        var lifted = _lifted;
        // Only the beginning is the track's to say: the chord lasts until the next mark.
        SegmentMoved?.Invoke(this, (clip.Index, _clipStart, _clipTo));
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), () => { if (_lifted == lifted) Land(); });   // a move the song turned down
    }

    /// <summary>The chords are back from the song: once the ribbon has drawn
    /// them, the lifted chord is put down.</summary>
    private void ChordsArrived()
    {
        if (_lifted == null || _clipDragging) return;
        var lifted = _lifted;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => { if (_lifted == lifted) Land(); });
    }

    private void Land()
    {
        if (_lifted == null) return;
        _lifted.Face.IsVisible = false;
        _lifted = null;
        _hole.IsVisible = false;
        Follow();                                                // the badges take their places again
    }

    /// <summary>Every chord on screen gets its badge; the rest wait their turn.
    /// Nothing moves while one is off the track.</summary>
    private void PlaceClips()
    {
        if (_lifted != null) return;
        var segments = Segments;
        if (!Editing || segments == null || Width <= 0)
        {
            foreach (var clip in _clips)
                if (clip.Host.IsVisible) { clip.Host.IsVisible = false; clip.Index = -1; }
            return;
        }
        double w = Width, pps = PixelsPerSecond, px = w * PlayheadAt, pos = ViewAt;
        int used = 0;
        for (int i = 0; i < segments.Count && used < MostClips; i++)
        {
            if (segments[i].Label == "—") continue;
            double x = px + (segments[i].Start - pos) * pps;
            if (x < -PillMaxWidth * 2 || x > w + PillMaxWidth) continue;
            var clip = used < _clips.Count ? _clips[used] : NewClip();
            used++;
            clip.Index = i;
            PlaceClip(clip, segments[i].Start);
        }
        for (int k = used; k < _clips.Count; k++)
            if (_clips[k].Host.IsVisible) { _clips[k].Host.IsVisible = false; _clips[k].Index = -1; }
    }

    /// <summary>Over the badge the ribbon drew: the same width, and the same
    /// nudge the no-overlap rule gave it — centred on its moment once lifted.</summary>
    private void PlaceClip(Block clip, double start)
    {
        var segments = Segments;
        if (segments == null || clip.Index < 0 || clip.Index >= segments.Count) return;
        double width = _labels.TryGetValue(segments[clip.Index].Label, out var measured) ? measured.Width : 46;
        double shift = clip == _lifted ? -width / 2 : _pillShift.TryGetValue(clip.Index, out var nudge) ? nudge : -width / 2;
        double x = Width * PlayheadAt + (start - ViewAt) * PixelsPerSecond + shift;
        if (Math.Abs(clip.Host.WidthRequest - width) > 0.5) clip.Host.WidthRequest = width;
        bool handles = clip.Index == Selected && clip != _lifted;
        if (clip.Handles.IsVisible != handles) clip.Handles.IsVisible = handles;
        if (!clip.Host.IsVisible) clip.Host.IsVisible = true;
        NativeTransform.TranslateX(clip.Host, x);
    }

    private void TapAt(Point point0)
    {
        Point? p = point0;
        var segments = Segments;
        // The pinned "next" pill at the right edge.
        if (_pinned.IsVisible && _nextIndex >= 0 && segments != null && p.Value.X >= Width - PillMaxWidth - 14 && p.Value.Y <= PillTop + PillHeight + 10)
        {
            SeekRequested?.Invoke(this, segments[_nextIndex].Start);
            return;
        }
        // Pills on the ribbon (ribbon coordinates = screen minus the translation).
        var point = new PointF((float)(p.Value.X - _tx), (float)p.Value.Y);
        foreach (var (rect, start, index) in _pills[_active])
            if (rect.Contains(point))
            {
                // In the editor a chord is chosen, not jumped to: the playhead is
                // the reader's to move and the chord being worked on is theirs to keep.
                if (Editing) { Untether(); SelectionRequested?.Invoke(this, index); }
                else SeekRequested?.Invoke(this, start);
                return;
            }
        // Beside the chords the song goes there, and the chosen chord stays chosen
        // (user request 2026-09-14).
        double t = ViewAt + (p.Value.X - Width * PlayheadAt) / PixelsPerSecond;
        SeekRequested?.Invoke(this, Math.Clamp(t, 0, Math.Max(0, Duration)));
    }

    // ---- rendering ----------------------------------------------------------

    private static string Clock(double seconds)
    {
        int total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Pill width and its one or two lines, measured once per label.</summary>
    private (float Width, string Top, string? Bottom) Measure(ICanvas canvas, string label)
    {
        if (_labels.TryGetValue(label, out var cached)) return cached;
        var bold = Microsoft.Maui.Graphics.Font.DefaultBold;
        float one = canvas.GetStringSize(label, bold, PillFont).Width;
        (float, string, string?) result;
        if (one + 22f <= PillMaxWidth)
        {
            result = (Math.Max(46f, one + 22f), label, null);
        }
        else
        {
            // Split after the root (C, C#, Bb …) — "C#m7" reads as "C#" over "m7".
            int split = label.Length > 1 && (label[1] == '#' || label[1] == 'b' || label[1] == '♯' || label[1] == '♭') ? 2 : 1;
            string top = label[..split], bottom = label[split..];
            float w = Math.Max(canvas.GetStringSize(top, bold, PillFont - 3f).Width,
                               canvas.GetStringSize(bottom, bold, PillFont - 3f).Width);
            result = (Math.Clamp(w + 18f, 46f, PillMaxWidth + 12f), top, bottom);
        }
        _labels[label] = result;
        return result;
    }

    private enum PillStyle { Resting, Current, Pinned }

    private void DrawPill(ICanvas canvas, float left, string label, PillStyle style)
    {
        var (w, top, bottom) = Measure(canvas, label);
        var pill = new RectF(left, PillTop, w, PillHeight);
        canvas.FillColor = style switch
        {
            PillStyle.Current => Accent,
            PillStyle.Pinned => PinnedColor,
            _ => PillColor,
        };
        canvas.FillRoundedRectangle(pill, 13f);
        canvas.FontColor = style switch
        {
            PillStyle.Current => OnAccent,
            PillStyle.Pinned => PinnedTextColor,
            _ => TextColor,
        };
        if (bottom == null)
        {
            canvas.FontSize = PillFont;
            canvas.DrawString(top, pill, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
        else
        {
            canvas.FontSize = PillFont - 3f;
            canvas.DrawString(top, left, PillTop + 4f, w, PillHeight / 2, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.DrawString(bottom, left, PillTop + PillHeight / 2 - 2f, w, PillHeight / 2, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>One bar per fixed slice of the song, so bars never change height.</summary>
    private void EnsureBars()
    {
        var peaks = Peaks;
        _barSeconds = (BarWidth + BarGap) / Math.Max(1, PixelsPerSecond);
        if (peaks is not { Length: > 0 }) { _bars = Array.Empty<float>(); return; }
        int fps = Math.Max(1, PeaksFps);
        int count = (int)Math.Ceiling(peaks.Length / (double)fps / _barSeconds);
        var bars = new float[Math.Max(1, count)];
        for (int k = 0; k < bars.Length; k++)
        {
            int i0 = Math.Max(0, (int)(k * _barSeconds * fps));
            int i1 = Math.Min(peaks.Length - 1, (int)((k + 1) * _barSeconds * fps));
            int peak = 0;
            for (int i = i0; i <= i1; i++) if (peaks[i] > peak) peak = peaks[i];
            bars[k] = peak / 255f;
        }
        _bars = bars;
    }

    /// <summary>A ribbon: everything static about the song over one buffered
    /// window, in ribbon coordinates (x = (t − t0) · pps); bars in the played
    /// or the coming colour.</summary>
    private sealed class RibbonDrawable(ChordTrack track, int index, bool played) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            // The platform can still call Draw while the window is being torn
            // down, on a canvas whose session is already gone.
            if (track.Handler == null) return;                        // torn down: nothing to draw for
            try { DrawCore(canvas, rect); }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }

        private void DrawCore(ICanvas canvas, RectF rect)
        {
            var t = track;
            double t0 = t._t0[index];
            if (double.IsNaN(t0)) return;
            double pps = t.PixelsPerSecond;
            double tMin = Math.Max(0, t0 - Lead / pps), tMax = t0 + (rect.Width - Lead) / pps;
            float waveTop = rect.Top + PillTop + PillHeight + 16f;
            float waveBottom = rect.Bottom - 34f;                            // room for the ruler and its seconds
            float cy = (waveTop + waveBottom) / 2, half = (waveBottom - waveTop) / 2;
            float X(double time) => (float)((time - t0) * pps) + Lead;

            // (The A–B loop is not drawn here: it is a box over the ribbon, so
            // that it can grow with the playhead while the loop is being taken
            // without redrawing three screens of ribbon.)

            // Waveform bars, one colour per variant.
            if (t._bars == null) t.EnsureBars();
            var bars = t._bars!;
            if (bars.Length > 0)
            {
                int k0 = Math.Max(0, (int)Math.Floor(tMin / t._barSeconds));
                int k1 = Math.Min(bars.Length - 1, (int)Math.Ceiling(tMax / t._barSeconds));
                // Editing, the wave is one colour: the playhead is often out of
                // the window, and a track half in the accent then says nothing
                // (user request 2026-09-10).
                canvas.FillColor = played && !t.Editing ? (t._playedBar ??= t.Accent.WithAlpha(0.55f)) : t.WaveColor;
                for (int k = k0; k <= k1; k++)
                {
                    float x = X(k * t._barSeconds);
                    float h = Math.Max(3f, bars[k] * half);
                    canvas.FillRectangle(x, cy - h, BarWidth, h * 2);
                }
            }
            else
            {
                canvas.StrokeColor = t.WaveColor.WithAlpha(0.5f);
                canvas.StrokeSize = 1.5f;
                canvas.DrawLine(rect.Left, cy, rect.Right, cy);
            }

            // Beat ruler: beats, then bars with their seconds.
            var beats = t.Beats;
            if (beats is { Length: > 0 })
            {
                int start = Array.BinarySearch(beats, tMin);
                if (start < 0) start = ~start;
                canvas.StrokeSize = 1f;
                canvas.StrokeColor = t._beatTick ??= t.LineColor.WithAlpha(0.4f);
                for (int i = start; i < beats.Length && beats[i] <= tMax; i++)
                {
                    if (i % 4 == 0) continue;
                    float x = X(beats[i]);
                    canvas.DrawLine(x, waveBottom + 5, x, waveBottom + 10);
                }
                canvas.StrokeColor = t._barTick ??= t.LineColor.WithAlpha(0.8f);
                canvas.FontSize = 11f;
                canvas.FontColor = t.LineColor;
                for (int i = start; i < beats.Length && beats[i] <= tMax; i++)
                {
                    if (i % 4 != 0) continue;
                    float x = X(beats[i]);
                    canvas.DrawLine(x, waveBottom + 5, x, waveBottom + 14);
                    if (!t._clocks.TryGetValue(i, out var label)) t._clocks[i] = label = Clock(beats[i]);
                    // Box taller than the line (Core Text draws nothing into a box the line does not fit).
                    canvas.DrawString(label, x - 22f, waveBottom + 13f, 44f, 20f, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }

            // Chord pills at their moment, never overlapping the one before.
            var pills = t._pills[index];
            pills.Clear();
            var segments = t.Segments;
            if (segments is { Count: > 0 })
            {
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                float lastRight = float.MinValue;
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    if (seg.Label == "—") continue;
                    float x = X(seg.Start);
                    if (x < rect.Left - PillMaxWidth || x > rect.Right + PillMaxWidth) continue;
                    var (w, _, _) = t.Measure(canvas, seg.Label);
                    float left = x - w / 2;
                    if (left < lastRight + 3f) left = lastRight + 3f;
                    lastRight = left + w;
                    t._pillShift[i] = left - x;
                    // Resting, unless the editor is on and this is the chord being worked on.
                    var style = t.Editing && i == t.Selected ? PillStyle.Current : PillStyle.Resting;
                    t.DrawPill(canvas, left, seg.Label, style);
                    pills.Add((new RectF(left, 0, w, PillTop + PillHeight + 8f), seg.Start, i));
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            if (played) t._drawnPlayed[index] = t0; else t._drawnComing[index] = t0;   // this window is on the canvas
        }
    }

    /// <summary>The chord on the finger or pulsing: its own pill, chosen as it is on the ribbon.</summary>
    private sealed class LiftDrawable(ChordTrack track, Block block) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (track.Handler == null) return;                        // torn down: nothing to draw for
            try
            {
                var segments = track.Segments;
                if (segments == null || block.Index < 0 || block.Index >= segments.Count) return;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                track.DrawPill(canvas, 0f, segments[block.Index].Label, PillStyle.Current);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
        }
    }

    /// <summary>Dotted handles at both ends of the chord being worked on: the
    /// sign for "this moves". Bars read as "this stretches" (user request 2026-09-14).</summary>
    private sealed class HandlesDrawable(ChordTrack track) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (track.Handler == null || rect.Width <= 0) return;      // torn down: nothing to draw for
            try
            {
                const float Dot = 1.6f, Gap = 5.5f, Inset = 7f;
                float middle = PillTop + PillHeight / 2;
                canvas.FillColor = track.OnAccent.WithAlpha(0.85f);
                foreach (float x in new[] { Inset, rect.Width - Inset })
                    for (int k = -1; k <= 1; k++)
                        canvas.FillCircle(x, middle + k * Gap, Dot);
            }
            catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
        }
    }

    /// <summary>The chord being played, in the accent, over its pill on the ribbon.</summary>
    private sealed class CurrentDrawable(ChordTrack track) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (track.Handler == null) return;                        // torn down: nothing to draw for
            try
            {
                var t = track;
                var segments = t.Segments;
                if (t._currentIndex < 0 || segments == null || t._currentIndex >= segments.Count) return;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                t.DrawPill(canvas, 0f, segments[t._currentIndex].Label, PillStyle.Current);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }
    }

    /// <summary>The next chord, pinned to the right edge while its own spot is off screen.</summary>
    private sealed class PinnedDrawable(ChordTrack track) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (track.Handler == null) return;                        // torn down: nothing to draw for
            try { DrawCore(canvas, rect); }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }

        private void DrawCore(ICanvas canvas, RectF rect)
        {
            var t = track;
            var segments = t.Segments;
            if (t._nextIndex < 0 || segments == null || t._nextIndex >= segments.Count) return;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var label = segments[t._nextIndex].Label;
            var (w, _, _) = t.Measure(canvas, label);
            t.DrawPill(canvas, rect.Right - w - 2f, label, PillStyle.Pinned);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        }
    }
}

using Strunika.Mobile.Theme;
using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The conveyor: the song's waveform under a fixed playhead, every chord a
/// pill at the moment it starts, the next chord pinned to the right edge until
/// it enters the frame. Tap a pill = seek to that chord, drag = scrub.
/// <para>
/// Nothing is drawn per frame. The ribbon (bars, beat ruler, pills) is cut
/// into tiles a little under a phone's width, each rendered once into its own
/// canvas and only <i>translated</i> as the song plays. A tile that has gone
/// off one side is drawn again for the stretch coming up on the other while
/// that stretch is still out of sight — one tile at a time, never two in a
/// frame. Bars change colour at the playhead without any drawing: over the
/// ribbon lies a second row of tiles with nothing but the bars in the "played"
/// colour, inside a container clipped to the left of the playhead, sliding
/// along with it.
/// <para>
/// (Until 2026-09-17 the ribbon was two canvases three screens wide, each in
/// two colourings, and the spare pair was rendered whole every few seconds:
/// some twenty megabytes of pixels and every chord name twice in one frame,
/// which the phone showed as a skipped frame and a jump of the track. Before
/// that, redrawing sixty times a second made the Windows XAML runtime induce a
/// full garbage collection about once a second.)
/// </para>
/// </summary>
public sealed class ChordTrack : Grid
{
    private static void Redraw(BindableObject b, object? o, object? n) => ((ChordTrack)b).Redraw();
    private static void Rebuild(BindableObject b, object? o, object? n) { var t = (ChordTrack)b; t._bars = null; t._layoutDirty = true; t.Redraw(); t.Follow(); }
    private static void Reloop(BindableObject b, object? o, object? n) => ((ChordTrack)b).UpdateLoop();

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(nameof(Position), typeof(double), typeof(ChordTrack), 0.0, propertyChanged: (b, _, _) => ((ChordTrack)b).Follow());
    public static readonly BindableProperty DurationProperty = BindableProperty.Create(nameof(Duration), typeof(double), typeof(ChordTrack), 0.0);
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(ChordTrack), null, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t._currentIndex = -1; t._nextIndex = -1; t._layoutDirty = true; t.Redraw(); t.Follow(); t.ChordsArrived(); });
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
    public static readonly BindableProperty EditingProperty = BindableProperty.Create(nameof(Editing), typeof(bool), typeof(ChordTrack), false, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t.ModeChanged(); if (!t.Editing) t.Land(); t.Redraw(); t.Follow(); });
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
    /// <summary>A tile of the ribbon, in points: a whole number of bars of the
    /// wave, so that a tile's edge falls in the gap between two of them and
    /// nothing is ever cut in half by it.</summary>
    private const double TileWidth = 30 * (BarWidth + BarGap);
    /// <summary>Where in that gap the edge falls.</summary>
    private const double Seam = BarGap / 2;
    /// <summary>A tile's canvas runs on past its own stretch by this much: a
    /// pill or a ruler's clock belongs to the tile its left edge is in, and is
    /// drawn whole there.</summary>
    private const float Over = PillMaxWidth + 14f;

    private readonly AbsoluteLayout _layers, _ribbon, _leftClip;
    private readonly GraphicsView _pinned;
    private readonly BoxView _playhead;
    /// <summary>One canvas and the tile of the ribbon it holds.</summary>
    private sealed class Tile
    {
        public required GraphicsView View;
        /// <summary>Which tile of the ribbon: it covers ribbon x from N × TileWidth − Seam.</summary>
        public int N = int.MinValue;
        /// <summary>The canvas shows tile N (its Draw has run since it was aimed).</summary>
        public bool Drawn;
        /// <summary>Aimed at a stretch that is in view and not drawn yet: kept
        /// transparent until it is, or the old stretch shows at the new place.</summary>
        public bool Hidden;
        /// <summary>What is on the canvas is another stretch's: aimed anew and not drawn since.</summary>
        public bool Stale;
    }
    /// <summary>The ribbon itself, and over it the bars in the played colour.</summary>
    private Tile[] _tiles = Array.Empty<Tile>(), _playedTiles = Array.Empty<Tile>();
    /// <summary>Where every pill lies on the ribbon (its left edge, NaN for no
    /// pill) and how wide it is — laid out once for the whole song, so a pill
    /// is in the same place whichever tile draws it and whatever lies over it.</summary>
    private double[] _pillLeft = Array.Empty<double>();
    private float[] _pillWidth = Array.Empty<float>();
    private bool _layoutDirty = true, _followQueued, _pinnedShown;
    private int _lateLogs, _pinHold;
    private readonly Dictionary<string, (float Width, string Top, string? Bottom)> _labels = new();
    private readonly Dictionary<int, string> _clocks = new();
    private float[]? _bars;
    private double _barSeconds;
    private int _currentIndex = -1, _nextIndex = -1;
    /// <summary>The pill being played, on its own small canvas that rides along
    /// with the ribbon. The ribbons draw every pill in the resting style and
    /// never change when the chord does: re-rendering three screens of ribbon
    /// to recolour one pill was a visible hitch on every chord change on the
    /// phone (CoreText is slow with text), the one stutter left in the song.</summary>
    private readonly GraphicsView _current;
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
        Settle();                                                // the map has the window now; a scrub the coast carried ends with it
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
        Settle();                                                // never a coast stopped with its scrub left open
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
    private const double RingSeconds = 0.25;
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
        _ribbon = new AbsoluteLayout { InputTransparent = true };
        _leftClip = new AbsoluteLayout { IsClippedToBounds = true, InputTransparent = true };
        // Over the loop band, not under it: the playhead is the one thing on
        // the track that must never be tinted by anything.
        _playhead = new BoxView { CornerRadius = 2, InputTransparent = true, WidthRequest = 4, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        _pinned = new GraphicsView { Drawable = new PinnedDrawable(this), IsVisible = false };
        _current = new GraphicsView { Drawable = new CurrentDrawable(this), IsVisible = false, InputTransparent = true };
        _layers.Add(_ribbon);
        _layers.Add(_leftClip);
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
            // A finger on a coasting track stops it the moment it touches, as a
            // scroll view's does (user request 2026-09-15); the scrub it was is
            // not over — it ends where the finger leaves it.
            Pressed = _ =>
            {
                if (_dragging != 0 || !_coasting) return;
                StopCoast();
                _stoppedCoast = true;
            },
            Started = _ =>
            {
                if (_dragging != 0) return;                      // a loop end has the finger
                if (_coasting) StopCoast();
                _stoppedCoast = false;                           // the finger moved on: a drag, not a tap that stopped a coast
                _panning = true;
                _swipe.Clear();
                Untether();
                _panStart = _panAt = ViewAt;
                // Editing, the finger moves the track and not the song, so the
                // song is left playing. Outside it the scrub begins here unless
                // one is already under way (the finger caught a coasting track):
                // deciding that by a flag left over from an earlier stop let a
                // later drag go on without a scrub, and the song, still
                // playing, took every position back — the track stood while the
                // chords flickered, and no drag moved it again (user report
                // 2026-09-15).
                if (!Editing && !_scrubbing) BeginScrub();
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
                EndScrub();
            },
            Tapped = pt =>
            {
                if (_dragging != 0) return;
                // A press that stopped a coast is only that: the scrub ends where
                // the track came to rest. Any other press that did not move: the
                // scrub ends where it began, then the tap seeks.
                if (_stoppedCoast) { _stoppedCoast = false; _panning = false; EndScrub(); return; }
                if (_panning) { _panning = false; EndScrub(); }
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
        Loaded += (_, _) => { Redraw(); Follow(); };                   // canvases exist now: render the first tiles
    }

    /// <summary>Re-render every ribbon and the pinned pill (data or colours changed).</summary>
    /// <summary>The moment at the playhead's place on screen.</summary>
    private double ViewAt => Editing ? _viewTime : Position;

    /// <summary>The track is still moving after a fling.</summary>
    public bool Coasting => _coasting;

    /// <summary>A scrub of the song is under way (the owner has paused for it):
    /// from the finger's press outside the editor until it is ended here.</summary>
    private bool _scrubbing;

    private void BeginScrub()
    {
        _scrubbing = true;
        ScrubStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The scrub ends where the track is — only if one is under way,
    /// so the owner never hears an end without a start.</summary>
    private void EndScrub()
    {
        if (!_scrubbing) return;
        _scrubbing = false;
        ScrubEnded?.Invoke(this, _panAt);
    }

    /// <summary>The editor came on or went off while the finger or a coast had
    /// the track: whatever was under way ends now. A coast that ran on into the
    /// editor took its scrub with it and never ended it, and the song — paused
    /// for the scrub — never played again (user report 2026-09-15).</summary>
    private void ModeChanged()
    {
        StopCoast();
        _stoppedCoast = false;
        _panning = false;
        EndScrub();
    }

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
        EndScrub();
    }

    /// <summary>The moment in the middle of the window: where a chord is put
    /// when one is added, and where the editor's cursor stands.</summary>
    public double CentreTime => ViewAt + Width * (0.5 - PlayheadAt) / Math.Max(1, PixelsPerSecond);

    public void Redraw()
    {
        foreach (var tile in _tiles) Invalidate(tile);
        foreach (var tile in _playedTiles) Invalidate(tile);
        try { _pinned.Invalidate(); _current.Invalidate(); }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }

    /// <summary>The same stretch again (the song or the colours changed): what is
    /// on the canvas stays up until the new drawing replaces it.</summary>
    private static void Invalidate(Tile tile)
    {
        if (tile.N == int.MinValue) return;
        tile.Drawn = false;
        try { tile.View.Invalidate(); }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }

    /// <summary>The pill's left edge relative to its chord's moment: what the
    /// no-overlap rule made of it, or centred while nothing is laid out yet.</summary>
    private double Shift(int index, double width)
    {
        var segments = Segments;
        if (!_layoutDirty && segments != null && index >= 0 && index < _pillLeft.Length && index < segments.Count && !double.IsNaN(_pillLeft[index]))
            return _pillLeft[index] - segments[index].Start * PixelsPerSecond;
        return -width / 2;
    }

    /// <summary>Something the overlays are placed by has changed inside a Draw
    /// (the layout, a tile come into view): follow once more when it is over.</summary>
    private void FollowSoon()
    {
        if (_followQueued) return;
        _followQueued = true;
        Dispatcher.Dispatch(() => { _followQueued = false; Follow(); });
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
        double px = w * PlayheadAt;
        // The played bars' container is the track's width and is moved so that
        // its right edge is the playhead (FollowCore).
        AbsoluteLayout.SetLayoutBounds(_ribbon, new Rect(0, 0, w, h));
        AbsoluteLayout.SetLayoutBounds(_leftClip, new Rect(0, 0, w, h));
        // As many tiles as can be in view at once, and two to be got ready on
        // either side before they are.
        _tiles = Pool(_ribbon, _tiles, (int)Math.Ceiling((w + Over) / TileWidth) + 3, h, played: false);
        _playedTiles = Pool(_leftClip, _playedTiles, (int)Math.Ceiling(px / TileWidth) + 3, h, played: true);
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
        Redraw();
        Follow();
    }

    /// <summary>The row's canvases: made once, more only if the track grows.</summary>
    private Tile[] Pool(AbsoluteLayout host, Tile[] tiles, int count, double height, bool played)
    {
        if (tiles.Length < count)
        {
            var grown = new Tile[count];
            tiles.CopyTo(grown, 0);
            for (int i = tiles.Length; i < count; i++)
            {
                var view = new GraphicsView { InputTransparent = true, BackgroundColor = Colors.Transparent, Opacity = 0 };
                var tile = new Tile { View = view, Hidden = true };
                view.Drawable = new TileDrawable(this, tile, played);
                host.Add(view);
                grown[i] = tile;
            }
            tiles = grown;
        }
        foreach (var tile in tiles) AbsoluteLayout.SetLayoutBounds(tile.View, new Rect(0, 0, TileWidth + Over, height));
        return tiles;
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
        // Ribbon x (song seconds × the zoom) at the track's left edge.
        double origin = pos * pps - px;
        bool busy = false;
        foreach (var tile in _tiles) busy |= tile.N != int.MinValue && !tile.Drawn;
        foreach (var tile in _playedTiles) busy |= tile.N != int.MinValue && !tile.Drawn;
        Aim(_tiles, origin - Over, origin + w, origin, 0, ref busy);
        // Editing, the wave is one colour (see the tile's Draw): no played bars.
        if (_leftClip.IsVisible == Editing) _leftClip.IsVisible = !Editing;
        NativeTransform.TranslateX(_leftClip, px - w);
        if (!Editing) Aim(_playedTiles, origin, origin + px, origin, px - w, ref busy);

        var segments = Segments;
        int current = IndexAt(Position), next = NextAfter(Position);
        if (current != _currentIndex)
        {
            // Only the small canvas changes; the ribbons stay as they are.
            _currentIndex = current;
            _pinHold = 8;                                        // the pinned pill's turn comes a few frames on: one drawing a frame
            bool show = current >= 0 && segments != null && current < segments.Count && segments[current].Label != "—";
            if (_current.IsVisible != show) _current.IsVisible = show;
            if (show) _current.Invalidate();
        }
        if (_current.IsVisible && segments != null && _currentIndex >= 0 && _currentIndex < segments.Count)
        {
            // Over the very pill the ribbon drew, wherever the no-overlap rule put it.
            var seg = segments[_currentIndex];
            float pillW = _labels.TryGetValue(seg.Label, out var cm) ? cm.Width : 46f;
            NativeTransform.TranslateX(_current, px + (seg.Start - ViewAt) * pps + Shift(_currentIndex, pillW));
        }
        UpdateLoop();
        PlaceClips();
        if (_hole.IsVisible) PlaceHole();                        // the empty place rides along with the window
        if (_cursor.IsVisible != Editing) _cursor.IsVisible = _cursorMark.IsVisible = Editing;
        if (Editing)
        {
            if (_current.IsVisible) _current.IsVisible = false;   // the blocks say it better
            if (_pinned.IsVisible) { _pinned.IsVisible = false; _pinnedShown = false; }
            PlaceRing(segments, px, pos, pps);
            return;
        }
        if (_ringIndex >= 0 || _ringShown >= 0) { _ringIndex = -1; HideRingNow(); }
        if (_pinHold > 0) _pinHold--;
        if (next != _nextIndex)
        {
            if (_pinHold > 0 && _pinned.IsVisible) { _pinned.IsVisible = false; _pinnedShown = false; }
            if (_pinHold > 0) { FollowSoon(); return; }          // (nothing below but the pinned pill; asked for again in case the song stands still)
            _nextIndex = next;
            _pinnedShown = false;
            _pinned.Invalidate();
        }
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
    /// Every tile that has any of ribbon x <paramref name="from"/>…<paramref name="to"/>
    /// in it gets a canvas, and every canvas is moved to where its tile now is.
    /// A canvas is taken from the tiles furthest out of the way. One in view is
    /// drawn at once, whatever else is going on (a seek, a drag across the
    /// song); one not yet in view — the next on either side — only while no
    /// other canvas is waiting for its Draw, so two are never drawn in a frame.
    /// </summary>
    /// <param name="inset">The x of the canvases' container on the track.</param>
    private void Aim(Tile[] tiles, double from, double to, double origin, double inset, ref bool busy)
    {
        if (tiles.Length == 0) return;
        int first = (int)Math.Floor((from + Seam) / TileWidth), last = (int)Math.Floor((to + Seam) / TileWidth);
        last = Math.Min(last, first + tiles.Length - 3);         // never more than the pool was made for
        for (int n = first; n <= last; n++)
            if (Holding(tiles, n) == null)
            {
                var tile = Spare(tiles, first, last);
                if (tile == null) break;
                bool late = tile.N != int.MinValue && IsLoaded;
                Take(tile, n, hide: true);
                busy = true;
                if (late && _lateLogs++ < 20) Strunika.Core.Diagnostics.FileLog.Info($"conveyor: tile {n} drawn in view");
            }
        if (!busy)
            foreach (int n in (ReadOnlySpan<int>)[last + 1, first - 1])
                if (Holding(tiles, n) == null && Spare(tiles, first - 1, last + 1) is { } tile)
                {
                    Take(tile, n, hide: false);
                    busy = true;
                    break;
                }
        foreach (var tile in tiles)
        {
            if (tile.N == int.MinValue) continue;
            if (tile.Hidden && tile.Drawn) { tile.Hidden = false; tile.View.Opacity = 1; }
            // Got ready out of sight, and come into sight without its Draw having
            // run (a platform that does not draw what it cannot see): never the
            // old stretch at the new place — hidden, and asked for again.
            if (tile.Stale && !tile.Hidden && tile.N >= first && tile.N <= last)
            {
                tile.Hidden = true;
                tile.View.Opacity = 0;
                try { tile.View.Invalidate(); } catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
                if (_lateLogs++ < 20) Strunika.Core.Diagnostics.FileLog.Info($"conveyor: tile {tile.N} was not drawn out of sight");
            }
            NativeTransform.TranslateX(tile.View, tile.N * TileWidth - Seam - origin - inset);
        }
    }

    private static Tile? Holding(Tile[] tiles, int n)
    {
        foreach (var tile in tiles) if (tile.N == n) return tile;
        return null;
    }

    /// <summary>The canvas whose tile is furthest outside <paramref name="first"/>…<paramref name="last"/>.</summary>
    private static Tile? Spare(Tile[] tiles, int first, int last)
    {
        Tile? best = null;
        long furthest = 0;
        foreach (var tile in tiles)
        {
            long away = tile.N == int.MinValue ? long.MaxValue : tile.N < first ? (long)first - tile.N : tile.N > last ? (long)tile.N - last : 0;
            if (away > furthest) { furthest = away; best = tile; }
        }
        return best;
    }

    private static void Take(Tile tile, int n, bool hide)
    {
        tile.N = n;
        tile.Drawn = false;
        tile.Stale = true;
        try
        {
            // Out of sight it needs no hiding; and one that was hidden stays so
            // until it is drawn.
            if (hide && !tile.Hidden) { tile.Hidden = true; tile.View.Opacity = 0; }
            tile.View.Invalidate();
        }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
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
        double x = px + (segment.Start - pos) * pps + Shift(shown, width);
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
            double middle = segments[i].Start + (Shift(i, width) + width / 2) / pps;
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
        bool clip = _clipDragging && _clipMoved && _lifted != null;
        if ((_dragging == 0 && !clip) || w <= 0) return;
        const double Zone = 64, Fastest = 280;                   // points, points per second
        const double Patience = 3, Impatient = 4;                // seconds, then up to this many times as fast
        double x = clip ? _clipPressX + _clipDx : _dragPressX + _dragDx;
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
        if (clip)
        {
            // In the editor it is the window that moves, the song plays on: the
            // chord keeps its place under the finger while the track goes by.
            double was = _viewTime;
            _viewTime = Math.Clamp(was + speed * 0.05 / PixelsPerSecond, Lowest, Math.Max(0, Duration));
            if (Math.Abs(_viewTime - was) < 1e-6) return;        // the song has no more to give that way
            _clipScrolled += _viewTime - was;
            Follow();
            DragClip(_lifted!);
            return;
        }
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
    /// <summary>Where the finger took the chord, on screen, and how far the
    /// window has been pulled along since: a chord held near an edge scrolls
    /// the track under it, as a loop end does (user request 2026-09-15).</summary>
    private double _clipPressX, _clipScrolled;
    /// <summary>The empty place a lifted chord left: its moment and its nudge,
    /// so the hole rides along when the window moves.</summary>
    private double _holeStart, _holeShift;
    /// <summary>The chord off the track: from its first move until the song's
    /// chords come back with the move in them.</summary>
    private Block? _lifted;
    /// <summary>Counts the lifts. A timer set to put a chord down belongs to the
    /// lift it was set in: the pooled view is often the same one for the next
    /// drag, and a timer from the drag before put that chord down in the middle
    /// of it — its face hidden, its place filled in again, nothing left to see
    /// move (user report 2026-09-17).</summary>
    private int _liftTurn;

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
        _clipScrolled = 0;
        double width = PillWidth(segments[clip.Index].Label);
        double shift = Shift(clip.Index, width);
        _clipPressX = Width * PlayheadAt + (_clipFrom - ViewAt) * PixelsPerSecond + shift + width / 2;
        _edgeSince = 0;
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
        double moved = BeatMath.Snap(Beats ?? Array.Empty<double>(), _clipFrom + _clipDx / PixelsPerSecond + _clipScrolled);
        // Anywhere in the song: the neighbours are no walls — the song puts the
        // chord down wherever it lands and whatever is there gives way (user
        // report 2026-09-14).
        _clipStart = Math.Clamp(moved, 0, Math.Max(0, Duration - MinClip));
        PlaceClip(clip, _clipStart);
        (_scroller ??= NewScroller()).Start();                   // near an edge, the window comes along
    }

    /// <summary>The chord comes away from its place: the place left empty, and
    /// the chord itself on the finger in its own colours, with a pulse. The buzz
    /// came with the hold before it (user request 2026-09-14).</summary>
    private void Lift(Block clip)
    {
        var segments = Segments;
        if (segments == null || clip.Index < 0 || clip.Index >= segments.Count) return;
        _lifted = clip;
        _liftTurn++;
        double width = _labels.TryGetValue(segments[clip.Index].Label, out var measured) ? measured.Width : 46;
        double shift = Shift(clip.Index, width);
        _hole.WidthRequest = width + 2;
        _holeStart = segments[clip.Index].Start;
        _holeShift = shift - 1;
        PlaceHole();
        _hole.IsVisible = true;
        clip.Handles.IsVisible = false;
        clip.Face.IsVisible = true;
        clip.Face.Invalidate();
        Pulse(clip);
    }

    private void PlaceHole() =>
        NativeTransform.TranslateX(_hole, Width * PlayheadAt + (_holeStart - ViewAt) * PixelsPerSecond + _holeShift);

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
        _scroller?.Stop();
        _edgeSince = 0;
        if (!_clipMoved) return;
        _clipMoved = false;
        // The chord stays lifted where it was let go, and its old place empty,
        // until the song's chords come back with the move in them: putting
        // either down first showed the old badge again for a frame.
        int turn = _liftTurn;
        // Only the beginning is the track's to say: the chord lasts until the next mark.
        SegmentMoved?.Invoke(this, (clip.Index, _clipStart, _clipTo));
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), () => { if (_liftTurn == turn && !_clipDragging) Land(); });   // a move the song turned down
    }

    /// <summary>The chords are back from the song: once the ribbon has drawn
    /// them, the lifted chord is put down.</summary>
    private void ChordsArrived()
    {
        if (_lifted == null || _clipDragging) return;
        int turn = _liftTurn;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => { if (_liftTurn == turn && !_clipDragging) Land(); });
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
        double shift = clip == _lifted ? -width / 2 : Shift(clip.Index, width);
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
        // Pills on the ribbon, where the layout put them.
        if (segments != null && !_layoutDirty && p.Value.Y <= PillTop + PillHeight + 8)
        {
            double x = ViewAt * PixelsPerSecond + p.Value.X - Width * PlayheadAt;
            for (int index = 0; index < segments.Count && index < _pillLeft.Length; index++)
            {
                double left = _pillLeft[index];
                if (double.IsNaN(left) || x < left || x > left + _pillWidth[index]) continue;
                // In the editor a chord is chosen, not jumped to: the playhead is
                // the reader's to move and the chord being worked on is theirs to keep.
                if (Editing) { Untether(); SelectionRequested?.Invoke(this, index); }
                else SeekRequested?.Invoke(this, segments[index].Start);
                return;
            }
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

    /// <summary>Every pill's place on the ribbon, once for the whole song: at
    /// its moment, never overlapping the one before. It takes a canvas to
    /// measure the names, so it is done in the first Draw that needs it.</summary>
    private void EnsureLayout(ICanvas canvas)
    {
        var segments = Segments;
        int count = segments?.Count ?? 0;
        if (!_layoutDirty && _pillLeft.Length == count) return;
        var lefts = new double[count];
        var widths = new float[count];
        double pps = PixelsPerSecond, lastRight = double.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            var seg = segments![i];
            if (seg.Label == "—") { lefts[i] = double.NaN; continue; }
            var (w, _, _) = Measure(canvas, seg.Label);
            double left = Math.Max(seg.Start * pps - w / 2, lastRight + 3);
            lastRight = left + w;
            lefts[i] = left;
            widths[i] = w;
        }
        _pillLeft = lefts;
        _pillWidth = widths;
        _layoutDirty = false;
        FollowSoon();                                            // what lies over the pills moves to them
    }

    /// <summary>A tile of the ribbon: everything static about the song over its
    /// stretch, in the tile's own coordinates — or, for the row over it, only the
    /// bars, in the played colour.</summary>
    private sealed class TileDrawable(ChordTrack track, Tile tile, bool played) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            // The platform can still call Draw while the window is being torn
            // down, on a canvas whose session is already gone.
            if (track.Handler == null) return;                        // torn down: nothing to draw for
            long began = Services.DrawMeter.Begin();
            try { DrawCore(canvas, rect); Services.DrawMeter.End(began); }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }

        private void DrawCore(ICanvas canvas, RectF rect)
        {
            var t = track;
            int n = tile.N;
            if (n == int.MinValue) return;
            double pps = t.PixelsPerSecond;
            // The stretch of the ribbon that is this tile's own.
            double left = n * TileWidth - Seam, right = left + TileWidth;
            float waveTop = rect.Top + PillTop + PillHeight + 16f;
            float waveBottom = rect.Bottom - 34f;                            // room for the ruler and its seconds
            float cy = (waveTop + waveBottom) / 2, half = (waveBottom - waveTop) / 2;
            float X(double ribbon) => (float)(ribbon - left);

            // (The A–B loop is not drawn here: it is a box over the ribbon, so
            // that it can grow with the playhead while the loop is being taken.)

            // Waveform bars. A bar is a fixed stretch of the ribbon whatever the
            // zoom, and a tile is a whole number of them.
            if (t._bars == null) t.EnsureBars();
            var bars = t._bars!;
            double pitch = BarWidth + BarGap;
            if (bars.Length > 0)
            {
                int k0 = Math.Max(0, (int)Math.Ceiling(left / pitch));
                int k1 = Math.Min(bars.Length - 1, (int)Math.Ceiling(right / pitch) - 1);
                // The played bars lie over the others and must hide them whole, so
                // their colour is the accent already mixed with the ground — what
                // a see-through accent over the ground came to. Editing, the wave
                // is one colour: the playhead is often out of the window, and a
                // track half in the accent then says nothing (user request 2026-09-10).
                canvas.FillColor = played ? (t._playedBar ??= Mix(t.Accent, t.BackdropColor, 0.55f)) : t.WaveColor;
                for (int k = k0; k <= k1; k++)
                {
                    float h = Math.Max(3f, bars[k] * half);
                    canvas.FillRectangle(X(k * pitch), cy - h, BarWidth, h * 2);
                }
            }
            else if (!played)
            {
                canvas.StrokeColor = t.WaveColor.WithAlpha(0.5f);
                canvas.StrokeSize = 1.5f;
                canvas.DrawLine(0, cy, (float)TileWidth, cy);
            }
            if (played) { Done(); return; }

            // Beat ruler: beats, then bars with their seconds. A clock belongs to
            // the tile its left edge is in.
            var beats = t.Beats;
            if (beats is { Length: > 0 })
            {
                const float ClockHalf = 22f;
                int start = Array.BinarySearch(beats, left / pps);
                if (start < 0) start = ~start;
                canvas.StrokeSize = 1f;
                canvas.StrokeColor = t._beatTick ??= t.LineColor.WithAlpha(0.4f);
                for (int i = start; i < beats.Length && beats[i] * pps < right; i++)
                {
                    if (i % 4 == 0) continue;
                    float x = X(beats[i] * pps);
                    canvas.DrawLine(x, waveBottom + 5, x, waveBottom + 10);
                }
                canvas.StrokeColor = t._barTick ??= t.LineColor.WithAlpha(0.8f);
                canvas.FontSize = 11f;
                canvas.FontColor = t.LineColor;
                for (int i = start; i < beats.Length && beats[i] * pps - ClockHalf < right; i++)
                {
                    if (i % 4 != 0) continue;
                    double at = beats[i] * pps;
                    float x = X(at);
                    if (at < right) canvas.DrawLine(x, waveBottom + 5, x, waveBottom + 14);
                    if (at - ClockHalf < left) continue;             // the tile before has it
                    if (!t._clocks.TryGetValue(i, out var label)) t._clocks[i] = label = Clock(beats[i]);
                    // Box taller than the line (Core Text draws nothing into a box the line does not fit).
                    canvas.DrawString(label, x - ClockHalf, waveBottom + 13f, ClockHalf * 2, 20f, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }

            // Chord pills where the layout put them; a pill belongs to the tile
            // its left edge is in.
            var segments = t.Segments;
            if (segments is { Count: > 0 })
            {
                t.EnsureLayout(canvas);
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                var lefts = t._pillLeft;
                for (int i = 0; i < segments.Count && i < lefts.Length; i++)
                {
                    double at = lefts[i];
                    if (double.IsNaN(at) || at < left) continue;
                    if (at >= right) break;                           // the lefts only grow
                    // Resting, unless the editor is on and this is the chord being worked on.
                    var style = t.Editing && i == t.Selected ? PillStyle.Current : PillStyle.Resting;
                    t.DrawPill(canvas, X(at), segments[i].Label, style);
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            Done();
        }

        /// <summary>This stretch is on the canvas.</summary>
        private void Done()
        {
            tile.Drawn = true;
            tile.Stale = false;
            if (tile.Hidden) track.FollowSoon();                 // shown by the next Follow — asked for, in case the song stands still
        }

        private static Color Mix(Color over, Color ground, float alpha) => new(
            over.Red * alpha + ground.Red * (1 - alpha),
            over.Green * alpha + ground.Green * (1 - alpha),
            over.Blue * alpha + ground.Blue * (1 - alpha));
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
                long began = Services.DrawMeter.Begin();
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                t.DrawPill(canvas, 0f, segments[t._currentIndex].Label, PillStyle.Current);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                Services.DrawMeter.End(began);
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
            long began = Services.DrawMeter.Begin();
            try { DrawCore(canvas, rect); Services.DrawMeter.End(began); }
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

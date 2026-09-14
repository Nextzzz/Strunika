using Strunika.Mobile.Theme;
using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The song as a grid of beats, whole bars to a row: every analysed beat is a
/// square, a beat where a chord starts carries its name, and the beat being
/// played is filled with the accent colour. Tapping a square asks the page to
/// seek there. An alternative to the conveyor for players who want to see the
/// bars ahead, not the seconds.
/// <para>
/// A row is always a whole number of bars — in 4/4 that is one bar on a small
/// phone and two or more as the screen grows, never a row that cuts a bar in
/// half. The squares keep a comfortable size instead of the count being fixed.
/// </para>
/// <para>
/// The grid is a stack of canvases, a band of rows each, not one tall canvas:
/// a four-minute song is taller than the largest bitmap a GPU will hand out
/// (Direct2D stops at 16384 px and threw outright). The beat being played is a
/// canvas of its own, one square in size, painted by the same code — so the
/// only drawing a beat change costs is that square.
/// </para>
/// </summary>
public sealed class BeatGrid : Grid
{
    /// <summary>Below this a square is too small for a chord name.</summary>
    private const float MinCell = 56f, Corner = 9f, Stroke = 1.5f;
    private const int MaxBarsPerRow = 4;
    /// <summary>Tallest band of rows one canvas may cover, in points. Kept well
    /// under the platform's bitmap limit at any display scale.</summary>
    private const float BandHeight = 1200f;

    public static readonly BindableProperty BeatsPerBarProperty =
        BindableProperty.Create(nameof(BeatsPerBar), typeof(int), typeof(BeatGrid), 4, propertyChanged: Rebuild);
    public static readonly BindableProperty BeatsProperty =
        BindableProperty.Create(nameof(Beats), typeof(double[]), typeof(BeatGrid), Array.Empty<double>(), propertyChanged: Rebuild);
    public static readonly BindableProperty SegmentsProperty =
        BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(BeatGrid), null, propertyChanged: Rebuild);
    public static readonly BindableProperty CellColorProperty =
        BindableProperty.Create(nameof(CellColor), typeof(Color), typeof(BeatGrid), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty ChordCellColorProperty =
        BindableProperty.Create(nameof(ChordCellColor), typeof(Color), typeof(BeatGrid), Colors.Gray, propertyChanged: Redraw);
    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(BeatGrid), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty AccentProperty =
        BindableProperty.Create(nameof(Accent), typeof(Color), typeof(BeatGrid), Colors.Goldenrod, propertyChanged: Redraw);
    public static readonly BindableProperty OnAccentProperty =
        BindableProperty.Create(nameof(OnAccent), typeof(Color), typeof(BeatGrid), Colors.Black, propertyChanged: Redraw);
    /// <summary>The beat whose chord is being worked on — a different mark from
    /// the beat being played, which the song moves and the reader does not.</summary>
    public static readonly BindableProperty ChosenProperty =
        BindableProperty.Create(nameof(Chosen), typeof(int), typeof(BeatGrid), -1, propertyChanged: (b, _, _) => { Redraw(b, null, null); ((BeatGrid)b).PlaceThumb(); });
    public static readonly BindableProperty LoopStartProperty =
        BindableProperty.Create(nameof(LoopStart), typeof(double), typeof(BeatGrid), -1.0, propertyChanged: Redraw);
    public static readonly BindableProperty LoopEndProperty =
        BindableProperty.Create(nameof(LoopEnd), typeof(double), typeof(BeatGrid), -1.0, propertyChanged: Redraw);
    /// <summary>The ground under the grid: the ink of a chord lifted off its square.</summary>
    public static readonly BindableProperty BackdropColorProperty =
        BindableProperty.Create(nameof(BackdropColor), typeof(Color), typeof(BeatGrid), Colors.Black, propertyChanged: Redraw);

    public Color BackdropColor { get => (Color)GetValue(BackdropColorProperty); set => SetValue(BackdropColorProperty, value); }

    /// <summary>Beats in a bar (the time signature's top number): a row holds
    /// whole bars, so this decides where the rows break.</summary>
    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
    public double[] Beats { get => (double[])GetValue(BeatsProperty); set => SetValue(BeatsProperty, value); }
    public IReadOnlyList<ChordSegmentDto>? Segments { get => (IReadOnlyList<ChordSegmentDto>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Color CellColor { get => (Color)GetValue(CellColorProperty); set => SetValue(CellColorProperty, value); }
    public Color ChordCellColor { get => (Color)GetValue(ChordCellColorProperty); set => SetValue(ChordCellColorProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    /// <summary>The chord name on the accent square.</summary>
    public Color OnAccent { get => (Color)GetValue(OnAccentProperty); set => SetValue(OnAccentProperty, value); }
    /// <summary>The A–B loop, so the squares inside it read as the part being
    /// worked on. There is no editing here — that lives on the conveyor.</summary>
    public double LoopStart { get => (double)GetValue(LoopStartProperty); set => SetValue(LoopStartProperty, value); }
    public double LoopEnd { get => (double)GetValue(LoopEndProperty); set => SetValue(LoopEndProperty, value); }

    private bool _onScreen, _stale;

    /// <summary>
    /// The page says which of the two views of the song is on screen. Hidden,
    /// the grid draws nothing at all: it remembers that something changed and
    /// catches up in one redraw the moment it is shown.
    /// <para>It is not the same view that is hidden — the scroll view around
    /// the grid is — so the grid cannot see this for itself. And it matters:
    /// invalidating a canvas still crosses into the native layer with nothing
    /// to show for it, and while the conveyor is on screen every loop end
    /// dragged there was doing that a dozen times over, which is what tore the
    /// drag apart after a visit to the grid (user report 2026-09-09).</para>
    /// </summary>
    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        if (!onScreen || !_stale) return;
        _stale = false;
        Redraw();
    }

    public int Chosen { get => (int)GetValue(ChosenProperty); set => SetValue(ChosenProperty, value); }

    private bool InLoop(double time)
    {
        double a = LoopStart, b = LoopEnd;
        return a >= 0 && b > a && time >= a - 1e-6 && time < b - 1e-6;
    }

    /// <summary>How many squares fit a row at this width, always whole bars.</summary>
    public int Columns { get; private set; } = 4;

    /// <summary>Tapped square → the time of its beat.</summary>
    public event EventHandler<double>? SeekRequested;
    /// <summary>A beat was tapped while the editor is on: its chord is chosen
    /// and the song is left where it is.</summary>
    public event EventHandler<int>? BeatChosen;
    /// <summary>The editor is on: a tap chooses instead of jumping, and the
    /// chord being worked on wears a handle it can be dragged by.</summary>
    public bool Editing
    {
        get => _editing;
        set
        {
            if (_editing == value) return;
            _editing = value;
            if (!value) PutDown();
            PlaceThumb();
            Redraw(this, null, null);                            // the chosen square's outline comes and goes with the editor
        }
    }

    private bool _editing;

    /// <summary>A chord was dragged from one square onto another.</summary>
    public event EventHandler<(int From, int To)>? ChordDropped;
    /// <summary>The chosen chord's square was tapped again (it wears the handle,
    /// so the tap lands there and not on the grid).</summary>
    public event EventHandler? ChosenTapped;

    /// <summary>Row height plus the gap under it: what a row of squares costs
    /// down the page. The page scrolls by it and maps the song by it.</summary>
    public double RowPitch => _cell + _gap;
    /// <summary>The cursor entered another row: (its row, the row's pitch) — the
    /// page scrolls by whole rows, so the top row is never left half cut.</summary>
    public event EventHandler<(int Row, double Step)>? ActiveMoved;

    // One face for both painters: the squares and the beat being played are drawn
    // by the same code, so a name cannot change weight when a beat goes active.
    /// <summary>Name size as a share of the square — the same on both sides.</summary>
    private const float CellFontRatio = 0.32f;
    /// <summary>The face the conveyor's chord pills use, so a chord reads the
    /// same whichever view of the song is on screen.</summary>
    private static readonly Microsoft.Maui.Graphics.Font CanvasFont = Microsoft.Maui.Graphics.Font.DefaultBold;

    private static void Rebuild(BindableObject b, object? o, object? n)
    {
        var grid = (BeatGrid)b;
        grid._labels = null;
        grid._holds = null;
        grid._active = -1;
        grid._liftedBeat = -1;                                   // the chords came back: whatever was lifted is down
        grid._thumbFull = false;
        grid._thumbFace.Invalidate();
        grid._bandRows = 0;                                      // the bands are re-cut on the next layout
        grid.InvalidateMeasure();
        grid.Layout(grid.Width);
    }

    /// <summary>Redraw when there is somebody to see it; otherwise remember to
    /// (see <see cref="SetOnScreen"/>).</summary>
    private static void Redraw(BindableObject b, object? o, object? n)
    {
        var grid = (BeatGrid)b;
        if (grid._bands.Count == 0) return;                      // nothing drawn yet: Layout will draw it
        if (grid._onScreen) grid.Redraw();
        else grid._stale = true;
    }

    private void Redraw()
    {
        foreach (var band in _bands) band.Invalidate();
        foreach (var cursor in _cursors) cursor.Invalidate();
        _thumbFace.Invalidate();
    }

    private readonly VerticalStackLayout _stack;
    private readonly List<GraphicsView> _bands = new();
    /// <summary>Two cursor canvases. The name sits in the middle of a square
    /// where a chord starts and in the corner where one is merely held, so
    /// moving a single canvas and asking for a repaint showed the old square's
    /// text at the new square's place for a frame — the name appeared to hop
    /// between corner and centre. The spare is drawn first and only shown once
    /// its paint has actually run.</summary>
    private readonly GraphicsView[] _cursors = new GraphicsView[2];
    /// <summary>The handle on the chord being worked on, and the outline of the
    /// square it would land on. A canvas cannot be dragged — and on iOS a pan
    /// never says where it began — so the one thing that can be taken hold of
    /// is a view of its own, sitting over the square (user request 2026-09-10).</summary>
    private readonly Grid _thumb;
    private readonly Border _drop;
    private readonly GraphicsView _thumbFace;
    /// <summary>The face draws the whole chosen square — while it pulses and
    /// while it is on the finger — and only the dotted handle otherwise.</summary>
    private bool _thumbFull;
    private int _pulseTurn;
    /// <summary>Where a hold on the grid came down, for the drag that follows it.</summary>
    private double _holdX, _holdY;
    /// <summary>A hold has just ended: a tap the platform may still send for it is not a tap.</summary>
    private bool _swallowTap;
    /// <summary>The square whose chord is off it — on the finger, or waiting
    /// where it was let go for the song's chords to come back — or −1.</summary>
    private int _liftedBeat = -1;
    private double _thumbX, _thumbY;
    private int _dragFrom = -1, _dragTo = -1;
    private readonly int[] _cursorBeat = { -1, -1 }, _cursorDrawn = { -1, -1 };
    private int _shown;                                          // the cursor on screen
    private int _pending = -1;                                   // beat the spare is being drawn for
    private string?[]? _labels, _holds;
    private int _active = -1, _cursorRow = -1, _bandRows;
    private float _cell, _gap = 6f, _cursorCell;

    public BeatGrid()
    {
        _stack = new VerticalStackLayout { Spacing = _gap };
        Children.Add(_stack);
        for (int i = 0; i < _cursors.Length; i++)
        {
            _cursors[i] = new GraphicsView
            {
                Drawable = new CursorDrawable(this, i),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true,
                // Both stay in the tree and are swapped by opacity: a hidden
                // GraphicsView is never painted, so a hidden spare could never
                // report that it had drawn — and the cursor stopped moving.
                Opacity = 0,
                IsVisible = false,
            };
            Children.Add(_cursors[i]);
        }

        _drop = new Border
        {
            StrokeThickness = 2, BackgroundColor = Colors.Transparent, Padding = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(Corner) },
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsVisible = false, InputTransparent = true,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        };
        _thumbFace = new GraphicsView { Drawable = new ThumbDrawable(this), InputTransparent = true, BackgroundColor = Colors.Transparent };
        var thumbTouch = new BoxView { Color = Colors.Transparent };
        // A plain grid holds the face, so the face can grow past its square in a
        // pulse: the host carries the transform that places it, the face its scale.
        _thumb = new Grid
        {
            IsVisible = false, Children = { _thumbFace, thumbTouch },
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        };
        Children.Add(_drop);
        Children.Add(_thumb);
        PointerDrag.Attach(thumbTouch, new PointerDrag.Callbacks
        {
            // Nothing happens until the finger actually moves: a press is not a drag.
            Started = _ => { if (_liftedBeat < 0) _dragFrom = _dragTo = Chosen; },
            Dragged = DragTo,
            Ended = EndDrag,
            Held = _ => { if (_liftedBeat >= 0) return; Services.Haptics.Default.Success(); Pulse(); },   // held on the chosen chord: it says so again
            Tapped = _ =>
            {
                bool lifted = _liftedBeat >= 0;
                EndDrag();
                if (!lifted) ChosenTapped?.Invoke(this, EventArgs.Empty);
            },
        });

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        _stack.GestureRecognizers.Add(tap);
        // Any chord can be taken from a hold, chosen or not (user request
        // 2026-09-14); until the hold takes, the scroll view keeps its scrolling.
        HoldDrag.Attach(_stack, new HoldDrag.Callbacks
        {
            Held = HoldAt,
            Moved = point => DragTo(point.X - _holdX, point.Y - _holdY),
            Released = _ => EndHold(),
            Cancelled = EndHold,
        });
        SizeChanged += (_, _) => Layout(Width);
    }

    /// <summary>The handle sits over the square the chord starts on, and is
    /// there only while that square holds a chord of its own — or while that
    /// chord is lifted, when the drag places it.</summary>
    private void PlaceThumb()
    {
        _labels ??= Beats.Length == 0 ? null : Labels();
        bool lifted = _liftedBeat >= 0;
        int chosen = Chosen;
        bool can = Editing && _cell > 0 && _labels != null
                   && (lifted || (chosen >= 0 && chosen < _labels.Length && _labels[chosen] != null));
        if (_thumb.IsVisible != can)
        {
            _thumb.IsVisible = can;
            if (can) _thumbFace.Invalidate();
        }
        if (!can)
        {
            if (_drop.IsVisible) _drop.IsVisible = false;
            return;
        }
        if (Math.Abs(_thumb.WidthRequest - _cell) > 0.5)
        {
            _thumb.WidthRequest = _thumb.HeightRequest = _cell;
            _drop.WidthRequest = _drop.HeightRequest = _cell;
        }
        _drop.Stroke = Accent;
        if (lifted) return;                                      // on the finger, or waiting where it was let go
        double step = _cell + _gap;
        _thumbX = chosen % Columns * step;
        _thumbY = chosen / Columns * step;
        NativeTransform.TranslateX(_thumb, _thumbX);
        NativeTransform.TranslateY(_thumb, _thumbY);
    }

    /// <summary>A finger held on a square with a chord of its own: that chord is
    /// chosen, with a tick and a pulse, and the drag that may follow takes it.</summary>
    private void HoldAt(Point point)
    {
        if (!Editing || _liftedBeat >= 0) return;
        int index = BeatAt(point.X, point.Y);
        if (index < 0) return;
        _labels ??= Labels();
        if (index >= _labels.Length || _labels[index] == null) return;
        _swallowTap = true;
        Services.Haptics.Default.Success();                      // the buzz marks the hold, not the drag (user rule 2026-09-14)
        Chosen = index;                                          // at once: the page confirms it on its next frame
        BeatChosen?.Invoke(this, index);
        PlaceThumb();
        Pulse();
        _dragFrom = _dragTo = index;
        _holdX = point.X;
        _holdY = point.Y;
    }

    private void EndHold()
    {
        EndDrag();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => _swallowTap = false);
    }

    /// <summary>The chosen chord grows a little and settles: the sign it was
    /// chosen, with no change of colour (user request 2026-09-14).</summary>
    private async void Pulse()
    {
        if (!_thumb.IsVisible) return;
        int turn = ++_pulseTurn;
        _thumbFull = true;
        _thumbFace.Invalidate();
        if (!Services.Motion.Reduced)
        {
            await _thumbFace.ScaleToAsync(1.12, 110, Easing.CubicOut);
            await _thumbFace.ScaleToAsync(1.0, 220, Easing.SpringOut);
        }
        if (turn != _pulseTurn || _liftedBeat >= 0) return;      // pulsed again, or on the finger now
        _thumbFull = false;
        _thumbFace.Invalidate();
    }

    private void DragTo(double dx, double dy)
    {
        if (_dragFrom < 0) return;
        if (_liftedBeat < 0) Lift();
        NativeTransform.TranslateX(_thumb, _thumbX + dx);
        NativeTransform.TranslateY(_thumb, _thumbY + dy);
        int target = BeatAt(_thumbX + dx + _cell / 2, _thumbY + dy + _cell / 2);
        if (target < 0 || target == _dragTo) return;
        _dragTo = target;
        double step = _cell + _gap;
        NativeTransform.TranslateX(_drop, target % Columns * step);
        NativeTransform.TranslateY(_drop, target / Columns * step);
    }

    /// <summary>The chord comes off its square: the square left empty, and the
    /// chord on the finger in its own colours; the buzz came with the hold
    /// before it (user request 2026-09-14).</summary>
    private void Lift()
    {
        _liftedBeat = _dragFrom;
        _drop.IsVisible = true;
        NativeTransform.TranslateX(_drop, _thumbX);
        NativeTransform.TranslateY(_drop, _thumbY);
        _thumbFull = true;
        _thumbFace.Invalidate();
        RepaintSquare(_liftedBeat);
    }

    private void EndDrag()
    {
        int from = _dragFrom, to = _dragTo;
        _dragFrom = _dragTo = -1;
        _drop.IsVisible = false;
        if (_liftedBeat < 0) return;                             // a press that never became a drag
        if (from >= 0 && to >= 0 && to != from)
        {
            // It waits, lifted, on the square it was let go on until the song's
            // chords come back with the move in them (Rebuild puts it down):
            // putting it down first showed it on its old square for a frame.
            double step = _cell + _gap;
            NativeTransform.TranslateX(_thumb, to % Columns * step);
            NativeTransform.TranslateY(_thumb, to / Columns * step);
            int lifted = _liftedBeat;
            ChordDropped?.Invoke(this, (from, to));
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), () => { if (_liftedBeat == lifted) PutDown(); });   // a move the song turned down
            return;
        }
        PutDown();
    }

    /// <summary>The chord back on its square: its name drawn there again, the handle over it.</summary>
    private void PutDown()
    {
        int beat = _liftedBeat;
        if (beat < 0) return;
        _liftedBeat = -1;
        _thumbFull = false;
        RepaintSquare(beat);
        _thumbFace.Invalidate();
        PlaceThumb();
    }

    /// <summary>The one band a square is painted on, and the cursor if it stands there.</summary>
    private void RepaintSquare(int beat)
    {
        if (beat < 0 || Columns <= 0) return;
        int band = _bandRows > 0 ? beat / Columns / _bandRows : 0;
        if (band < _bands.Count) _bands[band].Invalidate();
        if (beat == _active) foreach (var cursor in _cursors) cursor.Invalidate();
    }

    /// <summary>The square at a point on the grid, or −1 past the last beat.</summary>
    private int BeatAt(double x, double y)
    {
        double step = _cell + _gap;
        if (step <= 0 || x < 0 || y < 0) return -1;
        int col = (int)(x / step), row = (int)(y / step);
        if (col >= Columns) return -1;
        int index = row * Columns + col;
        return index >= 0 && index < Beats.Length ? index : -1;
    }

    /// <summary>Set every frame; only a beat change moves anything, and the
    /// spare canvas is shown here once its paint has landed.</summary>
    public double Position
    {
        set
        {
            var beats = Beats;
            if (beats.Length == 0) return;
            int index = Locate(beats, value);
            if (index != _active)
            {
                _active = index;
                PlaceCursor(force: false);
            }
            ShowWhenDrawn();
        }
    }

    /// <summary>The spare has painted the beat it was given: swap it in.</summary>
    private void ShowWhenDrawn()
    {
        if (_pending < 0) return;
        int spare = 1 - _shown;
        if (_cursorDrawn[spare] != _pending) return;
        _cursors[spare].Opacity = 1;
        _cursors[_shown].Opacity = 0;
        _shown = spare;
        _pending = -1;
    }

    /// <summary>The last beat at or before <paramref name="time"/>, −1 before the first.</summary>
    private static int Locate(double[] beats, double time)
    {
        int search = Array.BinarySearch(beats, time);
        return search >= 0 ? search : ~search - 1;
    }

    /// <summary>Whole bars to a row, squares no smaller than a chord name needs.</summary>
    private (int Columns, float Cell, float Gap) Geometry(double width)
    {
        var m = Theme.Metrics.Instance;
        float gap = (float)m.Size(6);
        int perBar = Math.Max(1, BeatsPerBar);
        float min = (float)m.Size(MinCell);
        int bars = Math.Clamp((int)Math.Floor((width + gap) / (perBar * (min + gap))), 1, MaxBarsPerRow);
        int columns = perBar * bars;
        return (columns, (float)((width - (columns - 1) * gap) / columns), gap);
    }

    /// <summary>
    /// Cut the song into bands of rows and give each its own canvas. The bands
    /// are stacked with exactly the gap between rows, so a row's position is
    /// row × pitch whichever band it lands in — which is what the cursor and the
    /// page's scrolling both assume.
    /// </summary>
    private void Layout(double width)
    {
        if (width <= 0) return;
        var beats = Beats;
        var (columns, cell, gap) = Geometry(width);
        int rows = beats.Length == 0 ? 0 : (beats.Length + columns - 1) / columns;
        int bandRows = Math.Max(1, (int)(BandHeight / (cell + gap)));
        int bands = (rows + bandRows - 1) / bandRows;
        // Any change in the cell at all re-cuts the bands: their heights and the
        // cursor's step have to come from one and the same number.
        bool sameShape = columns == Columns && Math.Abs(cell - _cell) < 0.01f && bandRows == _bandRows && bands == _bands.Count;

        Columns = columns;
        _cell = cell;
        _gap = gap;
        _bandRows = bandRows;
        _stack.Spacing = gap;

        if (!sameShape)
        {
            _bands.Clear();
            _stack.Children.Clear();
            for (int band = 0; band < bands; band++)
            {
                int first = band * bandRows;
                int count = Math.Min(bandRows, rows - first);
                _bands.Add(new GraphicsView
                {
                    Drawable = new BandDrawable(this, first),
                    HeightRequest = count * cell + (count - 1) * gap,
                });
                _stack.Children.Add(_bands[^1]);
            }
            // The grid's height is stated outright, and the scroll view above is
            // asked to measure again on the next turn: the bands are cut inside
            // the first layout pass (SizeChanged), and on iOS a measure
            // invalidated from within that pass was dropped — a song opened
            // straight into the grid view showed no squares at all, while
            // switching to it on the page (a fresh pass) always worked.
            HeightRequest = rows == 0 ? -1 : rows * cell + Math.Max(0, rows - 1) * gap;
            Dispatcher.Dispatch(() =>
            {
                if (Handler == null) return;
                InvalidateMeasure();
                (Parent as VisualElement)?.InvalidateMeasure();
                Redraw();
            });
        }
        Redraw();
        PlaceCursor(force: true);
        PlaceThumb();
    }

    private void PlaceCursor(bool force)
    {
        if (_cell <= 0 || _active < 0 || _active >= Beats.Length)
        {
            foreach (var cursor in _cursors) { cursor.IsVisible = false; cursor.Opacity = 0; }
            _pending = -1;
            return;
        }
        foreach (var cursor in _cursors) cursor.IsVisible = true;   // painted even while invisible to the eye
        double step = _cell + _gap;
        int row = _active / Columns;
        double x = _active % Columns * step, y = row * step;
        _labels ??= Labels();

        int spare = 1 - _shown;
        var back = _cursors[spare];
        // The size is re-applied whenever the cell changed, not only when the
        // caller says so: a cursor sized before the first layout stayed a dot.
        if (force || Math.Abs(_cursorCell - _cell) > 0.5)
        {
            _cursorCell = _cell;
            foreach (var cursor in _cursors) { cursor.WidthRequest = _cell; cursor.HeightRequest = _cell; }
        }
        back.TranslationX = x;
        back.TranslationY = y;
        _cursorBeat[spare] = _active;
        _cursorDrawn[spare] = -1;
        _pending = _active;
        back.Invalidate();                                       // one square's worth of drawing

        // Nothing on screen yet (a fresh page, or a jump): show it straight away
        // rather than waiting a frame for the swap.
        if (_cursors[_shown].Opacity == 0)
        {
            back.Opacity = 1;
            _shown = spare;
            _pending = -1;
        }
        bool newRow = row != _cursorRow;
        _cursorRow = row;
        if (newRow) ActiveMoved?.Invoke(this, (row, step));
    }

    /// <summary>Square cells in whole bars: the height follows the width.</summary>
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        double width = double.IsFinite(widthConstraint) ? widthConstraint : 360;
        int beats = Beats.Length;
        if (beats == 0) return new Size(width, 0);
        // Measure only. The bands and the cursor are both built from the width the
        // control actually gets (SizeChanged): computing here as well left the
        // bands sized by one cell and the cursor placed by another, and the two
        // walked apart a row at a time.
        var (columns, cell, gap) = Geometry(width);
        int rows = (beats + columns - 1) / columns;
        double height = rows * cell + (rows - 1) * gap;
        base.MeasureOverride(widthConstraint, height);
        return new Size(width, height);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_swallowTap) { _swallowTap = false; return; }       // the end of a hold, not a tap
        var beats = Beats;
        if (beats.Length == 0 || _cell <= 0 || e.GetPosition(_stack) is not { } point) return;
        double step = _cell + _gap;
        int col = Math.Clamp((int)(point.X / step), 0, Columns - 1);
        int row = (int)(point.Y / step);
        int index = row * Columns + col;
        if (index < 0 || index >= beats.Length) return;
        if (Editing)
        {
            // A chord's own square is chosen with a pulse; what an empty one means is the page's to say.
            _labels ??= Labels();
            if (index < _labels.Length && _labels[index] != null) { Chosen = index; PlaceThumb(); Pulse(); }
            BeatChosen?.Invoke(this, index);
        }
        else SeekRequested?.Invoke(this, beats[index]);
    }

    /// <summary>
    /// Chord name per beat: a segment marks the beat nearest its start. The
    /// second array is what is still sounding on a beat where nothing starts —
    /// the chord the player has to keep holding.
    /// </summary>
    private string?[] Labels()
    {
        var beats = Beats;
        var labels = new string?[beats.Length];
        var holds = new string?[beats.Length];
        if (Segments is { } segments)
            foreach (var segment in segments)
            {
                if (segment.Label == "—") continue;
                int at = BeatMath.Nearest(beats, segment.Start);   // the editor chooses by the same rule
                labels[at] ??= segment.Label;
                for (int i = at + 1; i < beats.Length && beats[i] < segment.End - 1e-6; i++) holds[i] ??= segment.Label;
            }
        _holds = holds;
        return labels;
    }

    /// <summary>
    /// One square: its ground, its accent outline, the chord that starts on it,
    /// and — for the beat being played — the chord still being held, in the
    /// corner. Every canvas calls this, which is what keeps them identical.
    /// </summary>
    private void DrawCell(ICanvas canvas, float x, float y, string? starts, string? holding, bool active, bool loop = false, bool chosen = false)
    {
        canvas.FillColor = active ? Accent : starts != null ? ChordCellColor : CellColor.WithAlpha(0.45f);
        canvas.FillRoundedRectangle(x, y, _cell, _cell, Corner);
        if (loop && !active)
        {
            // Inside the A–B loop: the accent laid over the square's own ground,
            // so a chord square inside it still reads as a chord square.
            canvas.FillColor = Accent.WithAlpha(0.18f);
            canvas.FillRoundedRectangle(x, y, _cell, _cell, Corner);
        }
        float stroke = chosen ? Stroke * 2.6f : Stroke;
        canvas.StrokeSize = stroke;
        float outline = starts != null ? 0.85f : 0.35f;
        if (loop) outline = Math.Max(outline, 0.7f);
        // The chord being worked on is outlined, not filled: filling it would be
        // the beat being played, and the two must never be mistaken.
        // In the accent, not the text colour: white on dark and black on light read
        // as a foreign frame (user request 2026-09-14).
        canvas.StrokeColor = chosen || active ? Accent : Accent.WithAlpha(outline);
        // Wholly inside the square: a thick outline drawn on the square's edge
        // was cut off by the canvas on the outer columns (user report 2026-09-14).
        canvas.DrawRoundedRectangle(x + stroke / 2, y + stroke / 2, _cell - stroke, _cell - stroke, Math.Max(2f, Corner - stroke / 2));

        var ink = active ? OnAccent : TextColor;
        if (starts != null)
        {
            float size = Fit(canvas, starts, Math.Max(8f, _cell * CellFontRatio), _cell - 6f);
            canvas.Font = CanvasFont;
            canvas.FontSize = size;
            canvas.FontColor = ink;
            canvas.DrawString(starts, x, y, _cell, _cell, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
        else if (holding != null)
        {
            // Nothing starts here, but this chord is still on: it goes in the
            // corner, small, so the square still reads as "hold what you have".
            float size = Fit(canvas, holding, Math.Max(9f, _cell * 0.24f), _cell * 0.62f);
            canvas.Font = CanvasFont;
            canvas.FontSize = size;
            canvas.FontColor = ink;
            canvas.DrawString(holding, x, y + _cell * 0.06f, _cell - _cell * 0.10f, size * 1.6f,
                              HorizontalAlignment.Right, VerticalAlignment.Top);
        }
    }

    /// <summary>Shrink a name to the room there is, never wrap it.</summary>
    private static float Fit(ICanvas canvas, string text, float size, float room)
    {
        float width = canvas.GetStringSize(text, CanvasFont, size).Width;
        return width > room && width > 0 ? Math.Max(7f, size * room / width) : size;
    }

    /// <summary>A band of rows: one canvas, small enough for any GPU.</summary>
    private sealed class BandDrawable(BeatGrid grid, int firstRow) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (grid.Handler == null || rect.Width <= 0) return;
            try
            {
                var beats = grid.Beats;
                if (beats.Length == 0) return;
                grid._labels ??= grid.Labels();
                float step = grid._cell + grid._gap;
                int first = firstRow * grid.Columns;
                int rows = (int)Math.Ceiling((rect.Height + grid._gap) / step);
                for (int i = first; i < beats.Length && i < first + rows * grid.Columns; i++)
                {
                    int local = i - first;
                    string? starts = i == grid._liftedBeat ? null : grid._labels[i];
                    grid.DrawCell(canvas, local % grid.Columns * step, local / grid.Columns * step,
                                  starts, null, active: false, loop: grid.InLoop(beats[i]), chosen: i == grid.Chosen);
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }
    }

    /// <summary>The handle on the chord being worked on — dotted, the sign for
    /// "this moves" (bars read as "this stretches") — and, while it pulses or
    /// rides the finger, the chosen square itself, drawn by the grid's own code
    /// in its own colours (user request 2026-09-14).</summary>
    private sealed class ThumbDrawable(BeatGrid grid) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (grid.Handler == null || rect.Width <= 0) return;
            try
            {
                float cell = rect.Width;
                if (grid._thumbFull)
                {
                    int beat = grid._liftedBeat >= 0 ? grid._liftedBeat : grid.Chosen;
                    var labels = grid._labels;
                    string? name = labels != null && beat >= 0 && beat < labels.Length ? labels[beat] : null;
                    grid.DrawCell(canvas, 0, 0, name, null, active: false, loop: false, chosen: true);
                    canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                }
                // Two dotted rows along the bottom of the square, clear of the name.
                canvas.FillColor = grid.TextColor.WithAlpha(0.85f);
                float radius = Math.Max(1.3f, cell * 0.022f), gap = radius * 3.4f;
                float middle = cell / 2, bottom = cell - Math.Max(6f, cell * 0.1f);
                for (int row = 0; row < 2; row++)
                    for (int k = -1; k <= 1; k++)
                        canvas.FillCircle(middle + k * gap, bottom - row * gap, radius);
            }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }
    }

    /// <summary>The one square being played, painted by the grid's own code.
    /// Each canvas paints the beat it was given, and records that it did — that
    /// record is what lets the page swap it in without a flash.</summary>
    private sealed class CursorDrawable(BeatGrid grid, int slot) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (grid.Handler == null) return;
            try
            {
                int beat = grid._cursorBeat[slot];
                var labels = grid._labels;
                if (labels == null || beat < 0 || beat >= labels.Length) return;
                string? starts = beat == grid._liftedBeat ? null : labels[beat];
                string? holding = starts == null && grid._holds != null && beat < grid._holds.Length
                    ? grid._holds[beat] : null;
                grid.DrawCell(canvas, 0, 0, starts, holding, active: true, loop: false, chosen: beat == grid.Chosen);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                grid._cursorDrawn[slot] = beat;
            }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }
    }
}

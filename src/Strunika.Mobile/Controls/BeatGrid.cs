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

    /// <summary>How many squares fit a row at this width, always whole bars.</summary>
    public int Columns { get; private set; } = 4;

    /// <summary>Tapped square → the time of its beat.</summary>
    public event EventHandler<double>? SeekRequested;
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
        grid._bandRows = 0;                                      // the bands are re-cut on the next layout
        grid.InvalidateMeasure();
        grid.Layout(grid.Width);
    }

    private static void Redraw(BindableObject b, object? o, object? n) => ((BeatGrid)b).Redraw();

    private void Redraw()
    {
        foreach (var band in _bands) band.Invalidate();
        foreach (var cursor in _cursors) cursor.Invalidate();
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

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        _stack.GestureRecognizers.Add(tap);
        SizeChanged += (_, _) => Layout(Width);
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
        }
        Redraw();
        PlaceCursor(force: true);
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
        var beats = Beats;
        if (beats.Length == 0 || _cell <= 0 || e.GetPosition(_stack) is not { } point) return;
        double step = _cell + _gap;
        int col = Math.Clamp((int)(point.X / step), 0, Columns - 1);
        int row = (int)(point.Y / step);
        int index = row * Columns + col;
        if (index >= 0 && index < beats.Length) SeekRequested?.Invoke(this, beats[index]);
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
                int at = Locate(beats, segment.Start + 1e-6);
                if (at + 1 < beats.Length && beats[at + 1] - segment.Start < segment.Start - beats[Math.Max(at, 0)]) at++;
                if (at < 0) at = 0;
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
    private void DrawCell(ICanvas canvas, float x, float y, string? starts, string? holding, bool active)
    {
        canvas.FillColor = active ? Accent : starts != null ? ChordCellColor : CellColor.WithAlpha(0.45f);
        canvas.FillRoundedRectangle(x, y, _cell, _cell, Corner);
        canvas.StrokeSize = Stroke;
        canvas.StrokeColor = active ? Accent : Accent.WithAlpha(starts != null ? 0.85f : 0.35f);
        canvas.DrawRoundedRectangle(x + Stroke / 2, y + Stroke / 2, _cell - Stroke, _cell - Stroke, Corner);

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
                    grid.DrawCell(canvas, local % grid.Columns * step, local / grid.Columns * step,
                                  grid._labels[i], null, active: false);
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
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
                string? starts = labels[beat];
                string? holding = starts == null && grid._holds != null && beat < grid._holds.Length
                    ? grid._holds[beat] : null;
                grid.DrawCell(canvas, 0, 0, starts, holding, active: true);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                grid._cursorDrawn[slot] = beat;
            }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }
    }
}

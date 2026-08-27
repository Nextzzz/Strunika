using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The conveyor: the song's waveform scrolls under a fixed playhead and every
/// chord is a marker at the moment it *starts* — not a bar spanning its whole
/// length. The chord that is playing sticks to the playhead, the one coming
/// next sticks to the right edge until it enters the frame, so the player
/// always sees what is now and what is next. Tapping a marker seeks to that
/// chord, dragging scrubs. Gestures live on a transparent overlay because a
/// bare GraphicsView gets no input on Windows.
/// </summary>
public sealed class ChordTrack : Grid, IDrawable
{
    private static void Redraw(BindableObject b, object? o, object? n) => ((ChordTrack)b)._canvas.Invalidate();
    private static void Rebuild(BindableObject b, object? o, object? n) { ((ChordTrack)b)._bars = null; Redraw(b, o, n); }

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(nameof(Position), typeof(double), typeof(ChordTrack), 0.0, propertyChanged: Redraw);
    public static readonly BindableProperty DurationProperty = BindableProperty.Create(nameof(Duration), typeof(double), typeof(ChordTrack), 0.0, propertyChanged: Redraw);
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(ChordTrack), null, propertyChanged: Redraw);
    public static readonly BindableProperty BeatsProperty = BindableProperty.Create(nameof(Beats), typeof(double[]), typeof(ChordTrack), null, propertyChanged: Redraw);
    public static readonly BindableProperty PeaksProperty = BindableProperty.Create(nameof(Peaks), typeof(byte[]), typeof(ChordTrack), null, propertyChanged: Rebuild);
    public static readonly BindableProperty PeaksFpsProperty = BindableProperty.Create(nameof(PeaksFps), typeof(int), typeof(ChordTrack), 40, propertyChanged: Rebuild);
    public static readonly BindableProperty PixelsPerSecondProperty = BindableProperty.Create(nameof(PixelsPerSecond), typeof(double), typeof(ChordTrack), 93.0, propertyChanged: Rebuild);
    public static readonly BindableProperty LoopStartProperty = BindableProperty.Create(nameof(LoopStart), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Redraw);
    public static readonly BindableProperty LoopEndProperty = BindableProperty.Create(nameof(LoopEnd), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Redraw);
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(nameof(Accent), typeof(Color), typeof(ChordTrack), Colors.Goldenrod, propertyChanged: Redraw);
    public static readonly BindableProperty OnAccentProperty = BindableProperty.Create(nameof(OnAccent), typeof(Color), typeof(ChordTrack), Colors.Black, propertyChanged: Redraw);
    public static readonly BindableProperty WaveColorProperty = BindableProperty.Create(nameof(WaveColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PillColorProperty = BindableProperty.Create(nameof(PillColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(ChordTrack), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(ChordTrack), Colors.Gray, propertyChanged: Redraw);

    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public IReadOnlyList<ChordSegmentDto>? Segments { get => (IReadOnlyList<ChordSegmentDto>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public double[]? Beats { get => (double[]?)GetValue(BeatsProperty); set => SetValue(BeatsProperty, value); }
    public byte[]? Peaks { get => (byte[]?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public int PeaksFps { get => (int)GetValue(PeaksFpsProperty); set => SetValue(PeaksFpsProperty, value); }
    /// <summary>The zoom: a fixed scale, so a wider screen simply shows more of
    /// the song rather than stretching the same seconds over it.</summary>
    public double PixelsPerSecond { get => (double)GetValue(PixelsPerSecondProperty); set => SetValue(PixelsPerSecondProperty, value); }
    public double LoopStart { get => (double)GetValue(LoopStartProperty); set => SetValue(LoopStartProperty, value); }
    public double LoopEnd { get => (double)GetValue(LoopEndProperty); set => SetValue(LoopEndProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Color OnAccent { get => (Color)GetValue(OnAccentProperty); set => SetValue(OnAccentProperty, value); }
    public Color WaveColor { get => (Color)GetValue(WaveColorProperty); set => SetValue(WaveColorProperty, value); }
    public Color PillColor { get => (Color)GetValue(PillColorProperty); set => SetValue(PillColorProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    /// <summary>Finger down: the owner pauses.</summary>
    public event EventHandler? ScrubStarted;
    /// <summary>Dragging (the owner applies the value).</summary>
    public event EventHandler<double>? Scrubbing;
    /// <summary>Finger up: seek here and resume if it was playing.</summary>
    public event EventHandler<double>? ScrubEnded;
    /// <summary>A tap: the start of the chord that was tapped, or the time under the finger.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Where the playhead sits across the width: a third in, so most
    /// of the track is the music still to come.</summary>
    private const float PlayheadAt = 0.25f;
    private const float BarWidth = 9f, BarGap = 3f, PillHeight = 44f, PillFont = 17f, PillMaxWidth = 64f;

    private readonly GraphicsView _canvas;
    private readonly List<(RectF Rect, double Start)> _pills = new();
    private readonly Dictionary<string, (float Width, string Top, string? Bottom)> _labels = new();
    /// <summary>Bar heights (0–1) on a fixed grid of <see cref="_barSeconds"/> —
    /// computed once per song, so a bar keeps its height while the track scrolls
    /// and every frame is nothing but drawing.</summary>
    private float[]? _bars;
    private double _barSeconds, _width;
    private double _panStart, _panAt;

    public ChordTrack()
    {
        _canvas = new GraphicsView { Drawable = this };
        Add(_canvas);
        var overlay = new BoxView { Color = Colors.Transparent };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTap;
        overlay.GestureRecognizers.Add(pan);
        overlay.GestureRecognizers.Add(tap);
        Add(overlay);
    }

    public void Redraw() => _canvas.Invalidate();

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStart = _panAt = Position;
                ScrubStarted?.Invoke(this, EventArgs.Empty);
                break;
            case GestureStatus.Running:
                // Never assign Position here: setting a bound property from code
                // clears its one-way binding and the conveyor stops following the
                // song. The owner applies the value and it comes back to us.
                _panAt = Math.Clamp(_panStart - e.TotalX / PixelsPerSecond, 0, Math.Max(0, Duration));
                Scrubbing?.Invoke(this, _panAt);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ScrubEnded?.Invoke(this, _panAt);
                break;
        }
    }

    private void OnTap(object? sender, TappedEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p == null) return;
        var point = new PointF((float)p.Value.X, (float)p.Value.Y);
        foreach (var (pill, start) in _pills)
            if (pill.Contains(point)) { SeekRequested?.Invoke(this, start); return; }
        double t = Position + (point.X - Width * PlayheadAt) / PixelsPerSecond;
        SeekRequested?.Invoke(this, Math.Clamp(t, 0, Math.Max(0, Duration)));
    }

    private static string Clock(double seconds)
    {
        int total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Pill width and its one or two lines. Measuring text is the most
    /// expensive call in a frame on Windows, so every label is measured once.</summary>
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

    /// <summary>One bar per fixed slice of the song. The grid is anchored to the
    /// song, not to the screen, so bars never change height as they scroll.</summary>
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

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Math.Abs(_width - rect.Width) > 0.5) { _width = rect.Width; _bars = null; }
        float px = rect.Left + rect.Width * PlayheadAt;
        double pps = PixelsPerSecond, pos = Position;
        double tMin = pos - (px - rect.Left) / pps, tMax = pos + (rect.Right - px) / pps;
        float waveTop = rect.Top + PillHeight + 16f;
        float waveBottom = rect.Bottom - 34f;   // room for the ruler and its seconds
        float cy = (waveTop + waveBottom) / 2, half = (waveBottom - waveTop) / 2;

        // A–B loop shading, behind everything.
        if (LoopStart >= 0 && LoopEnd > LoopStart)
        {
            float a = (float)(px + (LoopStart - pos) * pps), b = (float)(px + (LoopEnd - pos) * pps);
            float x0 = Math.Max(rect.Left, a), x1 = Math.Min(rect.Right, b);
            if (x1 > x0)
            {
                canvas.FillColor = Accent.WithAlpha(0.10f);
                canvas.FillRectangle(x0, rect.Top, x1 - x0, rect.Height);
            }
        }

        // Waveform: what has played in the accent, what is coming in the neutral colour.
        if (_bars == null) EnsureBars();
        var bars = _bars!;
        if (bars.Length > 0)
        {
            int k0 = Math.Max(0, (int)Math.Floor(tMin / _barSeconds));
            int k1 = Math.Min(bars.Length - 1, (int)Math.Ceiling(tMax / _barSeconds));
            for (int k = k0; k <= k1; k++)
            {
                float x = (float)(px + (k * _barSeconds - pos) * pps);
                float h = Math.Max(3f, bars[k] * half);
                canvas.FillColor = x + BarWidth / 2 <= px ? Accent.WithAlpha(0.55f) : WaveColor;
                canvas.FillRectangle(x, cy - h, BarWidth, h * 2);
            }
        }
        else
        {
            canvas.StrokeColor = WaveColor.WithAlpha(0.5f);
            canvas.StrokeSize = 1.5f;
            canvas.DrawLine(rect.Left, cy, rect.Right, cy);
        }

        // Beat ticks along the bottom.
        var beats = Beats;
        if (beats is { Length: > 0 })
        {
            int start = Array.BinarySearch(beats, tMin);
            if (start < 0) start = ~start;
            canvas.FontSize = 11f;
            canvas.FontColor = LineColor;
            for (int i = start; i < beats.Length && beats[i] <= tMax; i++)
            {
                float x = (float)(px + (beats[i] - pos) * pps);
                bool bar = i % 4 == 0;
                canvas.StrokeColor = LineColor.WithAlpha(bar ? 0.8f : 0.4f);
                canvas.StrokeSize = 1f;
                canvas.DrawLine(x, waveBottom + 5, x, waveBottom + 5 + (bar ? 9f : 5f));
                // Only the bar lines carry a time — a number over every beat is noise.
                if (bar) canvas.DrawString(Clock(beats[i]), x - 22f, waveBottom + 16f, 44f, 14f, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        // Chord markers at their start, plus the two sticky ones.
        _pills.Clear();
        var segments = Segments;
        float pillTop = rect.Top + 2f;
        if (segments is { Count: > 0 })
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            int currentIndex = -1, nextIndex = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                if (pos >= segments[i].Start && pos < segments[i].End) currentIndex = i;
                if (nextIndex < 0 && segments[i].Start > pos && segments[i].Label != "—") nextIndex = i;
            }
            float lastRight = float.MinValue;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Label == "—") continue;
                float x = (float)(px + (seg.Start - pos) * pps);
                bool current = i == currentIndex;
                var (w, top, bottom) = Measure(canvas, seg.Label);
                // Centred on the chord's own moment. The chord still to come is
                // pinned to the right edge until its true spot is on screen; a
                // chord that has scrolled past the left edge is simply gone.
                float lx = x - w / 2;
                if (i == nextIndex && lx > rect.Right - w) lx = rect.Right - w;
                if (lx + w < rect.Left || lx > rect.Right) continue;
                // Never overlap the marker before it (chords can stand shoulder to shoulder).
                if (lx < lastRight + 3f) lx = lastRight + 3f;
                lastRight = lx + w;

                var pill = new RectF(lx, pillTop, w, PillHeight);
                canvas.FillColor = current ? Accent : PillColor;
                canvas.FillRoundedRectangle(pill, 13f);
                canvas.FontColor = current ? OnAccent : TextColor;
                if (bottom == null)
                {
                    canvas.FontSize = PillFont;
                    canvas.DrawString(top, pill, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
                else
                {
                    // Long names go on two lines: markers can stand shoulder to
                    // shoulder, so width is the scarce thing here, not height.
                    canvas.FontSize = PillFont - 3f;
                    canvas.DrawString(top, lx, pillTop + 4f, w, PillHeight / 2, HorizontalAlignment.Center, VerticalAlignment.Center);
                    canvas.DrawString(bottom, lx, pillTop + PillHeight / 2 - 2f, w, PillHeight / 2, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
                _pills.Add((new RectF(lx, rect.Top, w, PillHeight + 6f), seg.Start));
            }
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        }

        // Playhead: a bar, not an arrow — starting under the markers.
        float headTop = pillTop + PillHeight + 4f;
        canvas.FillColor = Accent;
        canvas.FillRoundedRectangle(px - 2f, headTop, 4f, rect.Bottom - headTop, 2f);
    }
}

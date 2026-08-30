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

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(nameof(Position), typeof(double), typeof(ChordTrack), 0.0, propertyChanged: (b, _, _) => ((ChordTrack)b).Follow());
    public static readonly BindableProperty DurationProperty = BindableProperty.Create(nameof(Duration), typeof(double), typeof(ChordTrack), 0.0);
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(ChordTrack), null, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t._currentIndex = -1; t._nextIndex = -1; t.Redraw(); t.Follow(); });
    public static readonly BindableProperty BeatsProperty = BindableProperty.Create(nameof(Beats), typeof(double[]), typeof(ChordTrack), null, propertyChanged: (b, _, _) => { ((ChordTrack)b)._clocks.Clear(); ((ChordTrack)b).Redraw(); });
    public static readonly BindableProperty PeaksProperty = BindableProperty.Create(nameof(Peaks), typeof(byte[]), typeof(ChordTrack), null, propertyChanged: Rebuild);
    public static readonly BindableProperty PeaksFpsProperty = BindableProperty.Create(nameof(PeaksFps), typeof(int), typeof(ChordTrack), 40, propertyChanged: Rebuild);
    public static readonly BindableProperty PixelsPerSecondProperty = BindableProperty.Create(nameof(PixelsPerSecond), typeof(double), typeof(ChordTrack), 93.0, propertyChanged: Rebuild);
    public static readonly BindableProperty LoopStartProperty = BindableProperty.Create(nameof(LoopStart), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Redraw);
    public static readonly BindableProperty LoopEndProperty = BindableProperty.Create(nameof(LoopEnd), typeof(double), typeof(ChordTrack), -1.0, propertyChanged: Redraw);
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(nameof(Accent), typeof(Color), typeof(ChordTrack), Colors.Goldenrod, propertyChanged: (b, _, _) => ((ChordTrack)b).ApplyColours());
    public static readonly BindableProperty OnAccentProperty = BindableProperty.Create(nameof(OnAccent), typeof(Color), typeof(ChordTrack), Colors.Black, propertyChanged: Redraw);
    public static readonly BindableProperty WaveColorProperty = BindableProperty.Create(nameof(WaveColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PillColorProperty = BindableProperty.Create(nameof(PillColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PinnedColorProperty = BindableProperty.Create(nameof(PinnedColor), typeof(Color), typeof(ChordTrack), Colors.DimGray, propertyChanged: Redraw);
    public static readonly BindableProperty PinnedTextColorProperty = BindableProperty.Create(nameof(PinnedTextColor), typeof(Color), typeof(ChordTrack), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(ChordTrack), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(ChordTrack), Colors.Gray, propertyChanged: (b, _, _) => { var t = (ChordTrack)b; t._beatTick = null; t._barTick = null; t.Redraw(); });

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

    /// <summary>Finger down: the owner pauses.</summary>
    public event EventHandler? ScrubStarted;
    /// <summary>Dragging (the owner applies the value).</summary>
    public event EventHandler<double>? Scrubbing;
    /// <summary>Finger up: seek here and resume if it was playing.</summary>
    public event EventHandler<double>? ScrubEnded;
    /// <summary>A tap: the start of the chord that was tapped, or the time under the finger.</summary>
    public event EventHandler<double>? SeekRequested;

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
    private readonly List<(RectF Rect, double Start)>[] _pills = { new(), new() };   // ribbon coordinates, per buffer
    private int _active;
    private bool _pendingSwap, _pinnedShown;
    private int _pendingFrames;
    private readonly Dictionary<string, (float Width, string Top, string? Bottom)> _labels = new();
    private readonly Dictionary<int, string> _clocks = new();
    private float[]? _bars;
    private double _barSeconds;
    private double _tx;                                                     // translation of the visible ribbon
    private int _currentIndex = -1, _nextIndex = -1;
    private double _panStart, _panAt;
    private bool _panning;
    private Color? _beatTick, _barTick, _playedBar;

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
        _playhead = new BoxView { CornerRadius = 2, InputTransparent = true };
        _pinned = new GraphicsView { Drawable = new PinnedDrawable(this), IsVisible = false };
        _layers.Add(_leftClip);
        _layers.Add(_rightClip);
        _layers.Add(_playhead);
        _layers.Add(_pinned);
        Add(_layers);
        var overlay = new BoxView { Color = Colors.Transparent };
        Add(overlay);
        PointerDrag.Attach(overlay, new PointerDrag.Callbacks
        {
            Started = _ =>
            {
                _panning = true;
                _panStart = _panAt = Position;
                ScrubStarted?.Invoke(this, EventArgs.Empty);
            },
            Moved = dx =>
            {
                if (!_panning) return;
                // Never assign Position here: the owner applies it and it comes back.
                _panAt = Math.Clamp(_panStart - dx / PixelsPerSecond, 0, Math.Max(0, Duration));
                Scrubbing?.Invoke(this, _panAt);
            },
            Ended = () =>
            {
                if (!_panning) return;
                _panning = false;
                ScrubEnded?.Invoke(this, _panAt);
            },
            Tapped = pt =>
            {
                // A press that did not move: the scrub ends where it began, then the tap seeks.
                if (_panning) { _panning = false; ScrubEnded?.Invoke(this, _panAt); }
                TapAt(pt);
            },
        });
        ApplyColours();
        SizeChanged += (_, _) => Relayout();
        Loaded += (_, _) => { ResetBuffers(); Follow(); };             // canvases exist now: render the first window
    }

    /// <summary>Re-render every ribbon and the pinned pill (data or colours changed).</summary>
    public void Redraw()
    {
        for (int i = 0; i < 2; i++) { _played[i].Invalidate(); _coming[i].Invalidate(); }
        _pinned.Invalidate();
    }

    private void ResetBuffers()
    {
        for (int i = 0; i < 2; i++) _t0[i] = _drawnPlayed[i] = _drawnComing[i] = double.NaN;
        _pendingSwap = false;
    }

    private void ApplyColours()
    {
        _playhead.Color = Accent;
        _playedBar = null;
        Redraw();
    }

    private void Relayout()
    {
        double w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        double px = w * PlayheadAt, bw = w * BufferSpan + Lead;
        AbsoluteLayout.SetLayoutBounds(_leftClip, new Rect(0, 0, px, h));
        AbsoluteLayout.SetLayoutBounds(_rightClip, new Rect(px, 0, w - px, h));
        for (int i = 0; i < 2; i++)
        {
            AbsoluteLayout.SetLayoutBounds(_played[i], new Rect(0, 0, bw, h));
            AbsoluteLayout.SetLayoutBounds(_coming[i], new Rect(0, 0, bw, h));
        }
        AbsoluteLayout.SetLayoutBounds(_playhead, new Rect(px - 2, PillTop + PillHeight + 4, 4, h - PillTop - PillHeight - 6));
        AbsoluteLayout.SetLayoutBounds(_pinned, new Rect(w - PillMaxWidth - 14, 0, PillMaxWidth + 14, PillTop + PillHeight + 2));
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
        double pps = PixelsPerSecond, v = w / pps, px = w * PlayheadAt, pos = Position;
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
        for (int i = 0; i < 2; i++)
        {
            if (double.IsNaN(_t0[i])) continue;
            double tx = px - (pos - _t0[i]) * pps - Lead;
            NativeTransform.TranslateX(_played[i], tx);
            NativeTransform.TranslateX(_coming[i], tx - px);            // its container starts at the playhead
        }
        _tx = px - (pos - _t0[_active]) * pps - Lead;

        var segments = Segments;
        int current = IndexAt(pos), next = NextAfter(pos);
        if (current != _currentIndex)
        {
            _currentIndex = current;
            Invalidate(_active);
            if (_pendingSwap) Invalidate(back);
        }
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
        foreach (var (rect, start) in _pills[_active])
            if (rect.Contains(point)) { SeekRequested?.Invoke(this, start); return; }
        double t = Position + (p.Value.X - Width * PlayheadAt) / PixelsPerSecond;
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

    private void DrawPill(ICanvas canvas, float left, string label, bool current, bool pinned = false)
    {
        var (w, top, bottom) = Measure(canvas, label);
        var pill = new RectF(left, PillTop, w, PillHeight);
        canvas.FillColor = current ? Accent : pinned ? PinnedColor : PillColor;
        canvas.FillRoundedRectangle(pill, 13f);
        canvas.FontColor = current ? OnAccent : pinned ? PinnedTextColor : TextColor;
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

            // A–B loop shading.
            if (t.LoopStart >= 0 && t.LoopEnd > t.LoopStart)
            {
                float a = Math.Max(rect.Left, X(t.LoopStart)), b = Math.Min(rect.Right, X(t.LoopEnd));
                if (b > a)
                {
                    canvas.FillColor = t.Accent.WithAlpha(0.10f);
                    canvas.FillRectangle(a, rect.Top, b - a, rect.Height);
                }
            }

            // Waveform bars, one colour per variant.
            if (t._bars == null) t.EnsureBars();
            var bars = t._bars!;
            if (bars.Length > 0)
            {
                int k0 = Math.Max(0, (int)Math.Floor(tMin / t._barSeconds));
                int k1 = Math.Min(bars.Length - 1, (int)Math.Ceiling(tMax / t._barSeconds));
                canvas.FillColor = played ? (t._playedBar ??= t.Accent.WithAlpha(0.55f)) : t.WaveColor;
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
                    canvas.DrawString(label, x - 22f, waveBottom + 16f, 44f, 14f, HorizontalAlignment.Center, VerticalAlignment.Center);
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
                    t.DrawPill(canvas, left, seg.Label, i == t._currentIndex);
                    pills.Add((new RectF(left, 0, w, PillTop + PillHeight + 8f), seg.Start));
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            if (played) t._drawnPlayed[index] = t0; else t._drawnComing[index] = t0;   // this window is on the canvas
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
            t.DrawPill(canvas, rect.Right - w - 2f, label, current: false, pinned: true);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        }
    }
}

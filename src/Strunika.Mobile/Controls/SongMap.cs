using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The whole song in one strip, over the editor's track: where every chord
/// falls, where the playhead is, and which slice of it the track is looking
/// at. The track itself shows a few seconds at a time, so without this there
/// is no telling where in the song those seconds are (user request
/// 2026-09-10). A tap or a drag on it moves the track's window.
/// <para>Nothing here is redrawn as the song plays: the chords are painted
/// once onto a canvas, and the playhead and the window are views moved by
/// their transforms.</para>
/// </summary>
public sealed class SongMap : Grid
{
    private static void Repaint(BindableObject b, object? o, object? n) => ((SongMap)b)._rail.Invalidate();

    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(double), typeof(SongMap), 0.0, propertyChanged: (b, _, _) => { Repaint(b, o: null, n: null); ((SongMap)b).Place(); });
    public static readonly BindableProperty SegmentsProperty =
        BindableProperty.Create(nameof(Segments), typeof(IReadOnlyList<ChordSegmentDto>), typeof(SongMap), null, propertyChanged: Repaint);
    public static readonly BindableProperty RailColorProperty =
        BindableProperty.Create(nameof(RailColor), typeof(Color), typeof(SongMap), Colors.DimGray, propertyChanged: Repaint);
    public static readonly BindableProperty MarkColorProperty =
        BindableProperty.Create(nameof(MarkColor), typeof(Color), typeof(SongMap), Colors.Gray, propertyChanged: Repaint);
    public static readonly BindableProperty AccentProperty =
        BindableProperty.Create(nameof(Accent), typeof(Color), typeof(SongMap), Colors.Goldenrod, propertyChanged: (b, _, _) => ((SongMap)b).Recolour());

    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public IReadOnlyList<ChordSegmentDto>? Segments { get => (IReadOnlyList<ChordSegmentDto>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    /// <summary>The song's own line.</summary>
    public Color RailColor { get => (Color)GetValue(RailColorProperty); set => SetValue(RailColorProperty, value); }
    /// <summary>A chord's mark on it.</summary>
    public Color MarkColor { get => (Color)GetValue(MarkColorProperty); set => SetValue(MarkColorProperty, value); }
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    /// <summary>The reader asked the track to look here (the middle of the window).</summary>
    public event EventHandler<double>? ViewRequested;

    private readonly GraphicsView _rail;
    private readonly Border _window;
    private readonly BoxView _head, _pick;
    private readonly BoxView _gripLeft, _gripRight;
    private double _position, _viewStart, _viewSpan, _selection = -1;

    public SongMap()
    {
        HeightRequest = 48;                                      // a strip a finger can work with
        _rail = new GraphicsView { Drawable = new RailDrawable(this), InputTransparent = true };
        _window = new Border
        {
            StrokeThickness = 1.5, Padding = 0, InputTransparent = true,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Fill, WidthRequest = 1,
        };
        _head = new BoxView { WidthRequest = 2, CornerRadius = 1, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Fill, InputTransparent = true };
        // Where the chord being worked on sits in the song — the one thing the
        // reader needs to find again after letting the track go.
        _pick = new BoxView { WidthRequest = 4, CornerRadius = 2, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Fill, InputTransparent = true, IsVisible = false, Margin = new Thickness(0, 6) };
        // The window says it can be taken hold of.
        _gripLeft = Grip();
        _gripRight = Grip();
        Add(_rail);
        Add(_window);
        Add(_gripLeft);
        Add(_gripRight);
        Add(_pick);
        Add(_head);
        var touch = new BoxView { Color = Colors.Transparent };
        Add(touch);
        PointerDrag.Attach(touch, new PointerDrag.Callbacks
        {
            Started = x => _grabbed = x,
            Moved = dx => Ask(_grabbed + dx),
            Tapped = point => Ask(point.X),
        });
        Recolour();
        SizeChanged += (_, _) => { _rail.Invalidate(); Place(); };
    }

    private static BoxView Grip() => new()
    {
        WidthRequest = 3, HeightRequest = 16, CornerRadius = 1.5,
        HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center, InputTransparent = true,
    };

    private double _grabbed;

    /// <summary>Where the chord being worked on is, or below zero for none.</summary>
    public void Mark(double seconds)
    {
        if (Math.Abs(seconds - _selection) < 0.01) return;
        _selection = seconds;
        Place();
    }

    private void Ask(double x)
    {
        if (Width <= 0 || Duration <= 0) return;
        ViewRequested?.Invoke(this, Math.Clamp(x / Width, 0, 1) * Duration);
    }

    /// <summary>The slice the track is looking at, and where the song is.</summary>
    public void Show(double position, double viewStart, double viewSpan)
    {
        if (Math.Abs(position - _position) < 0.01 && Math.Abs(viewStart - _viewStart) < 0.01 && Math.Abs(viewSpan - _viewSpan) < 0.01) return;
        _position = position;
        _viewStart = viewStart;
        _viewSpan = viewSpan;
        Place();
    }

    private void Place()
    {
        double w = Width, duration = Duration;
        if (w <= 0 || duration <= 0) return;
        double scale = w / duration;
        double windowWidth = Math.Max(16, Math.Min(w, _viewSpan * scale));
        double left = Math.Clamp(_viewStart * scale, 0, Math.Max(0, w - windowWidth));
        NativeTransform.TranslateX(_window, left);
        NativeTransform.ScaleX(_window, windowWidth);            // the box is one point wide
        NativeTransform.TranslateX(_gripLeft, left + 3);
        NativeTransform.TranslateX(_gripRight, left + windowWidth - 6);
        NativeTransform.TranslateX(_head, Math.Clamp(_position * scale, 0, w - 2));
        bool marked = _selection >= 0;
        if (_pick.IsVisible != marked) _pick.IsVisible = marked;
        if (marked) NativeTransform.TranslateX(_pick, Math.Clamp(_selection * scale, 0, w - 4));
    }

    private void Recolour()
    {
        _window.BackgroundColor = Accent.WithAlpha(0.16f);
        _window.Stroke = Accent.WithAlpha(0.7f);
        _gripLeft.Color = _gripRight.Color = Accent.WithAlpha(0.8f);
        _head.Color = Accent;
        _pick.Color = MarkColor;
        _rail.Invalidate();
    }

    private sealed class RailDrawable(SongMap map) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (map.Handler == null || rect.Width <= 0) return;
            try
            {
                float mid = rect.Center.Y;
                canvas.StrokeSize = 2f;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.StrokeColor = map.RailColor;
                canvas.DrawLine(rect.Left + 1, mid, rect.Right - 1, mid);
                double duration = map.Duration;
                if (map.Segments is not { Count: > 0 } segments || duration <= 0) return;
                canvas.StrokeSize = 1.5f;
                canvas.StrokeColor = map.MarkColor.WithAlpha(0.7f);
                foreach (var segment in segments)
                {
                    if (segment.Label == "—") continue;
                    float x = (float)(rect.Left + segment.Start / duration * rect.Width);
                    canvas.DrawLine(x, mid - 8, x, mid + 8);
                }
            }
            catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
        }
    }
}

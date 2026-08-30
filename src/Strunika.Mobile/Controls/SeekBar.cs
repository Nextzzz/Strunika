namespace Strunika.Mobile.Controls;

/// <summary>
/// A position bar made of three plain views (track, fill, thumb) moved by
/// transforms — no native slider. Updating a WinUI Slider ten times a second
/// made the XAML runtime induce a full garbage collection roughly every
/// second (see <see cref="NativeTransform"/>); this costs the compositor two
/// numbers per frame.
/// </summary>
public sealed class SeekBar : Grid
{
    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(double), typeof(SeekBar), 0.0, propertyChanged: (b, _, _) => ((SeekBar)b).Apply());
    /// <summary>Two-way bindable value (0 … <see cref="Duration"/>) for use as a
    /// plain level control, e.g. volume with <c>Duration="1"</c>.</summary>
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double), typeof(SeekBar), 0.0, BindingMode.TwoWay, propertyChanged: (b, _, n) => ((SeekBar)b).OnValueChanged((double)n!));
    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(SeekBar), Colors.Gray, propertyChanged: (b, _, n) => ((SeekBar)b)._track.Color = (Color)n!);
    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(nameof(FillColor), typeof(Color), typeof(SeekBar), Colors.Goldenrod, propertyChanged: (b, _, n) => { var s = (SeekBar)b; s._fill.Color = (Color)n!; s._thumb.BackgroundColor = (Color)n!; });

    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public Color TrackColor { get => (Color)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public Color FillColor { get => (Color)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Seconds. Setting it while the thumb is being dragged is ignored.</summary>
    public double Position
    {
        get => _position;
        set { if (_dragging) return; Value = value; }
    }

    private void OnValueChanged(double value)
    {
        _position = value;
        Apply();
    }

    public event EventHandler? DragStarted;
    public event EventHandler<double>? Dragging;
    public event EventHandler<double>? DragCompleted;

    private const double ThumbSize = 22, TrackHeight = 4;
    private readonly BoxView _track, _fill;
    private readonly Border _thumb;
    private double _position, _dragStartPosition;
    private bool _dragging;

    public SeekBar()
    {
        HeightRequest = 32;
        _track = new BoxView { HeightRequest = TrackHeight, CornerRadius = TrackHeight / 2, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(ThumbSize / 2, 0), InputTransparent = true };
        _fill = new BoxView { HeightRequest = TrackHeight, CornerRadius = TrackHeight / 2, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(ThumbSize / 2, 0), AnchorX = 0, ScaleX = 0, InputTransparent = true };
        _thumb = new Border
        {
            WidthRequest = ThumbSize, HeightRequest = ThumbSize, StrokeThickness = 0, Padding = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(ThumbSize / 2) },
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center, InputTransparent = true,
        };
        Add(_track);
        Add(_fill);
        Add(_thumb);
        var overlay = new BoxView { Color = Colors.Transparent };
        Add(overlay);
        PointerDrag.Attach(overlay, new PointerDrag.Callbacks
        {
            Started = _ =>
            {
                _dragging = true;
                _dragStartPosition = _position;
                DragStarted?.Invoke(this, EventArgs.Empty);
            },
            Moved = dx =>
            {
                if (!_dragging || Duration <= 0) return;
                Value = Math.Clamp(_dragStartPosition + dx / Usable * Duration, 0, Duration);   // live, so a level is heard while dragging
                Dragging?.Invoke(this, _position);
            },
            Ended = () =>
            {
                if (!_dragging) return;
                _dragging = false;
                Value = _position;
                DragCompleted?.Invoke(this, _position);
            },
            Tapped = pt =>
            {
                // A press that did not move: seek there, through the same
                // pause → seek → resume path the owner uses for a drag.
                _dragging = false;
                if (Duration > 0) Value = TimeAt(pt.X);
                DragCompleted?.Invoke(this, _position);
            },
        });
        SizeChanged += (_, _) => Apply();
    }

    private double Usable => Math.Max(1, Width - ThumbSize);

    private void Apply()
    {
        if (Width <= 0) return;
        double p = Duration > 0 ? Math.Clamp(_position / Duration, 0, 1) : 0;
        NativeTransform.ScaleX(_fill, p);
        NativeTransform.TranslateX(_thumb, p * Usable);
    }

    private double TimeAt(double x) => Duration > 0 ? Math.Clamp((x - ThumbSize / 2) / Usable, 0, 1) * Duration : 0;
}

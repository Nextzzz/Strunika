using Strunika.Mobile.Services;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The tuner indicator: a guitar string that sags (flat) or bows up (sharp)
/// by the current cents and snaps straight, turning gold, when in tune.
/// Ticks mark ±50 ¢; the pale zone is the in-tune window.
/// </summary>
public sealed class TunerString : GraphicsView, IDrawable
{
    private const float PixelsPerCent = 3f;   // ±50 ¢ = ±150 pt, like the mockup
    private const float SagPerCent = 0.5f;

    public static readonly BindableProperty CentsProperty =
        BindableProperty.Create(nameof(Cents), typeof(double), typeof(TunerString), 0.0,
            propertyChanged: (b, o, n) => ((TunerString)b).AnimateTo((double)n));

    public static readonly BindableProperty InTuneProperty =
        BindableProperty.Create(nameof(InTune), typeof(bool), typeof(TunerString), false,
            propertyChanged: (b, o, n) => ((TunerString)b).OnInTuneChanged((bool)n));

    public static readonly BindableProperty HasSignalProperty =
        BindableProperty.Create(nameof(HasSignal), typeof(bool), typeof(TunerString), false,
            propertyChanged: (b, o, n) => ((TunerString)b).Invalidate());

    public static readonly BindableProperty StringColorProperty = Col(nameof(StringColor), Colors.Peru);
    public static readonly BindableProperty InTuneColorProperty = Col(nameof(InTuneColor), Colors.Gold);
    public static readonly BindableProperty IdleColorProperty = Col(nameof(IdleColor), Colors.Gray);
    public static readonly BindableProperty ZoneColorProperty = Col(nameof(ZoneColor), Colors.Transparent);
    public static readonly BindableProperty TickColorProperty = Col(nameof(TickColor), Colors.DimGray);
    public static readonly BindableProperty LabelColorProperty = Col(nameof(LabelColor), Colors.Gray);
    public static readonly BindableProperty BeadCoreColorProperty = Col(nameof(BeadCoreColor), Colors.Black);

    private static BindableProperty Col(string name, Color fallback) =>
        BindableProperty.Create(name, typeof(Color), typeof(TunerString), fallback,
            propertyChanged: (b, o, n) => ((TunerString)b).Invalidate());

    public double Cents { get => (double)GetValue(CentsProperty); set => SetValue(CentsProperty, value); }
    public bool InTune { get => (bool)GetValue(InTuneProperty); set => SetValue(InTuneProperty, value); }
    public bool HasSignal { get => (bool)GetValue(HasSignalProperty); set => SetValue(HasSignalProperty, value); }
    public Color StringColor { get => (Color)GetValue(StringColorProperty); set => SetValue(StringColorProperty, value); }
    public Color InTuneColor { get => (Color)GetValue(InTuneColorProperty); set => SetValue(InTuneColorProperty, value); }
    public Color IdleColor { get => (Color)GetValue(IdleColorProperty); set => SetValue(IdleColorProperty, value); }
    public Color ZoneColor { get => (Color)GetValue(ZoneColorProperty); set => SetValue(ZoneColorProperty, value); }
    public Color TickColor { get => (Color)GetValue(TickColorProperty); set => SetValue(TickColorProperty, value); }
    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }
    public Color BeadCoreColor { get => (Color)GetValue(BeadCoreColorProperty); set => SetValue(BeadCoreColorProperty, value); }

    private double _drawn;          // cents currently drawn (tweened toward Cents)
    private double _flash;          // 1 → 0 after locking in tune

    public TunerString()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        HeightRequest = 120;
        InputTransparent = true;
    }

    private void AnimateTo(double target)
    {
        this.AbortAnimation("cents");
        if (Motion.Reduced) { _drawn = target; Invalidate(); return; }
        double from = _drawn;
        new Animation(v => { _drawn = v; Invalidate(); }, from, target)
            .Commit(this, "cents", 16, 90, Easing.Linear);
    }

    /// <summary>Three slow golden pulses along the string (all strings tuned).</summary>
    public void Celebrate()
    {
        if (Motion.Reduced) return;
        this.AbortAnimation("flash");
        new Animation(v => { _flash = Math.Abs(Math.Sin(v * Math.PI * 3)); Invalidate(); }, 0, 1)
            .Commit(this, "flash", 16, 1500, Easing.Linear, (_, _) => { _flash = 0; Invalidate(); });
    }

    private void OnInTuneChanged(bool inTune)
    {
        if (inTune && !Motion.Reduced)
        {
            this.AbortAnimation("flash");
            new Animation(v => { _flash = v; Invalidate(); }, 1, 0)
                .Commit(this, "flash", 16, 600, Easing.CubicOut);
        }
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        // The platform can still call Draw while the window is being torn
        // down, on a canvas whose session is already gone; every call then
        // throws inside Maui.Graphics. There is nothing left to draw for.
        if (Handler == null) return;                                 // torn down: nothing to draw for
        try { DrawCore(canvas, rect); }
        catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
    }

    private void DrawCore(ICanvas canvas, RectF rect)
    {
        float w = rect.Width, h = rect.Height;
        if (w <= 0) return;
        float cx = w / 2, y0 = h * 0.42f;
        float span = Math.Min(150f, (w - 20) / 2);          // half-width for ±50 ¢
        float ppc = span / 50f;

        // ticks: every 12.5 ¢, taller at −50 / 0 / +50
        canvas.StrokeColor = TickColor;
        canvas.StrokeSize = 1.2f;
        canvas.StrokeLineCap = LineCap.Round;
        for (int i = -4; i <= 4; i++)
        {
            float x = cx + i * 12.5f * ppc;
            float tall = i == 0 ? 10 : Math.Abs(i) == 4 ? 8 : 5;
            canvas.DrawLine(x, h - 24, x, h - 24 + tall);
        }
        canvas.FontColor = LabelColor;
        canvas.FontSize = 11;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.DrawString("−50", cx - span - 16, h - 12, 32, 12, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.DrawString("0", cx - 16, h - 12, 32, 12, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.DrawString("+50", cx + span - 16, h - 12, 32, 12, HorizontalAlignment.Center, VerticalAlignment.Center);

        // in-tune zone
        float zoneW = 6f * ppc * 2;
        canvas.FillColor = ZoneColor;
        canvas.FillRoundedRectangle(cx - zoneW / 2, y0 - 18, zoneW, 36, 7);

        // the string: a parabola peaking at the bead (flat sags down, sharp bows up)
        bool live = HasSignal;
        double c = live ? _drawn : 0;
        float beadX = cx + (float)c * ppc;
        float sag = live && !InTune ? (float)(-c) * SagPerCent : 0f;   // flat (c<0) → positive → down
        // Both ends stay pinned on the axis (like a string on its nut and
        // bridge); the bump peaks at the bead — two parabolic halves.
        float left = 8, right = w - 8;
        float dLeft = Math.Max(1f, beadX - left), dRight = Math.Max(1f, right - beadX);
        var path = new PathF();
        path.MoveTo(left, y0);
        const int steps = 64;
        for (int i = 1; i <= steps; i++)
        {
            float x = left + (right - left) * i / steps;
            float u = x < beadX ? (beadX - x) / dLeft : (x - beadX) / dRight;
            float k = 1f - u * u;
            float y = y0 + sag * Math.Max(0f, k);
            path.LineTo(x, y);
        }

        var color = !live ? IdleColor : InTune ? InTuneColor : StringColor;
        if (InTune && _flash > 0)
        {
            canvas.StrokeColor = InTuneColor.WithAlpha((float)(0.45 * _flash));
            canvas.StrokeSize = 10;
            canvas.DrawPath(path);
        }
        canvas.StrokeColor = color;
        canvas.StrokeSize = InTune ? 3f : 2.6f;
        canvas.DrawPath(path);

        if (!live) return;

        // bead on the string
        float beadY = y0 + sag;
        if (InTune)
        {
            canvas.FillColor = InTuneColor.WithAlpha(0.35f);
            canvas.FillCircle(beadX, beadY, 15);
        }
        canvas.FillColor = InTune ? InTuneColor : StringColor;
        canvas.FillCircle(beadX, beadY, 9.5f);
        canvas.FillColor = BeadCoreColor;
        canvas.FillCircle(beadX, beadY, 3.5f);
    }
}

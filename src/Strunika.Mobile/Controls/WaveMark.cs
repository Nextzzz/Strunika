namespace Strunika.Mobile.Controls;

/// <summary>
/// The signature wave-through-a-string from the logo, drawn as a stroke
/// so it tints per theme. Used on the first-launch screen and empty states.
/// </summary>
public sealed class WaveMark : GraphicsView, IDrawable
{
    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(WaveMark), Colors.Goldenrod,
            propertyChanged: (b, o, n) => ((WaveMark)b).Invalidate());

    /// <summary>Stroke width in points (2.2 by default; the welcome wave uses 3.2 to match the lettering).</summary>
    public static readonly BindableProperty ThicknessProperty =
        BindableProperty.Create(nameof(Thickness), typeof(double), typeof(WaveMark), 2.2, propertyChanged: (b, _, _) => ((WaveMark)b).Invalidate());
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public static readonly BindableProperty GlowColorProperty =
        BindableProperty.Create(nameof(GlowColor), typeof(Color), typeof(WaveMark), Colors.Transparent,
            propertyChanged: (b, o, n) => ((WaveMark)b).Invalidate());

    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }

    /// <summary>Room kept around the string for its glow, in points: a canvas
    /// cannot paint outside itself, so a glowing wave the exact size of its box
    /// was cut off flat at the edges. Callers that glow give the control
    /// <see cref="Glow"/> more on every side (and pull the margins in by as
    /// much); the string itself keeps its size and place.</summary>
    public static readonly BindableProperty BleedProperty =
        BindableProperty.Create(nameof(Bleed), typeof(double), typeof(WaveMark), 0.0, propertyChanged: (b, _, _) => ((WaveMark)b).Invalidate());
    public double Bleed { get => (double)GetValue(BleedProperty); set => SetValue(BleedProperty, value); }

    /// <summary>The bleed a glowing wave wants: the glow is blurred 10 pt, and a
    /// blur fades over about two and a half times its radius — 12 pt cut it
    /// off where it was still plainly visible.</summary>
    public const double Glow = 26;

    // 342 × 64 design space, spikes left of centre like the logo.
    private static readonly PathF Shape = PathBuilder.Build(
        "M0 32H118c6 0 8-14 12-14s6 30 10 30 5-44 9-44 7 56 11 56 5-40 9-40 6 24 10 24 5-14 9-14 6 10 10 10c5 0 7-8 12-8H342");

    public WaveMark()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        HeightRequest = 64;
        InputTransparent = true;
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
        float bleed = (float)Bleed;
        rect = new RectF(rect.X + bleed, rect.Y + bleed, rect.Width - 2 * bleed, rect.Height - 2 * bleed);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        float k = rect.Width / 342f;
        canvas.SaveState();
        canvas.Translate(rect.X, rect.Y + (rect.Height - 64f * k) / 2);
        canvas.Scale(k, k);
        if (GlowColor.Alpha > 0)
            canvas.SetShadow(new SizeF(0, 0), 10, GlowColor);
        canvas.StrokeColor = Color;
        canvas.StrokeSize = (float)Thickness / k;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(Shape);
        canvas.RestoreState();
    }
}

namespace Strunika.Mobile.Controls;

/// <summary>Rolling bars of recent microphone peaks (the recording sheet).</summary>
public sealed class LevelMeter : GraphicsView
{
    private const int Bars = 36;
    private readonly float[] _levels = new float[Bars];

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(LevelMeter), Colors.Gold, propertyChanged: (b, _, _) => ((LevelMeter)b).Invalidate());
    public static readonly BindableProperty DimColorProperty =
        BindableProperty.Create(nameof(DimColor), typeof(Color), typeof(LevelMeter), Colors.Gray, propertyChanged: (b, _, _) => ((LevelMeter)b).Invalidate());

    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public Color DimColor { get => (Color)GetValue(DimColorProperty); set => SetValue(DimColorProperty, value); }

    public LevelMeter() => Drawable = new MeterDrawable(this);

    public void Push(float peak)
    {
        Array.Copy(_levels, 1, _levels, 0, Bars - 1);
        // Perceptual: −40 dB … 0 dB → 0 … 1.
        double db = 20 * Math.Log10(Math.Max(peak, 1e-5));
        _levels[Bars - 1] = (float)Math.Clamp((db + 40) / 40, 0, 1);
        Invalidate();
    }

    private sealed class MeterDrawable : IDrawable
    {
        private readonly LevelMeter _owner;
        public MeterDrawable(LevelMeter owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            // The platform can still call Draw while the window is being torn
            // down, on a canvas whose session is already gone; every call then
            // throws inside Maui.Graphics. There is nothing left to draw for.
            if (_owner?.Handler == null) return;                    // torn down: nothing to draw for
            try { DrawCore(canvas, rect); }
            catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        }

        private void DrawCore(ICanvas canvas, RectF rect)
        {
            float gap = 3f;
            float w = (rect.Width - gap * (Bars - 1)) / Bars;
            float mid = rect.Center.Y;
            for (int i = 0; i < Bars; i++)
            {
                float level = _owner._levels[i];
                float h = Math.Max(3f, level * rect.Height);
                float x = rect.Left + i * (w + gap);
                canvas.FillColor = level > 0.02f ? _owner.Color.WithAlpha(0.35f + 0.65f * (i / (float)Bars)) : _owner.DimColor;
                canvas.FillRoundedRectangle(x, mid - h / 2, w, h, w / 2);
            }
        }
    }
}

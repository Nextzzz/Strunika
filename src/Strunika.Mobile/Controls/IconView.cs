namespace Strunika.Mobile.Controls;

/// <summary>
/// A single tinted stroke icon (see <see cref="Icons"/>) or a flag.
/// <c>&lt;controls:IconView Name="lock" Color="{StaticResource TextSec}" Size="16" /&gt;</c>
/// </summary>
public sealed class IconView : GraphicsView, IDrawable
{
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(IconView), "", propertyChanged: Redraw);

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(IconView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(IconView), 24.0, propertyChanged: Resize);

    public static readonly BindableProperty StrokeSizeProperty =
        BindableProperty.Create(nameof(StrokeSize), typeof(double), typeof(IconView), 1.8, propertyChanged: Redraw);

    /// <summary>Token name from <see cref="Theme.Tokens"/> ("AccentText"); when set it
    /// overrides <see cref="Color"/> and follows theme changes — handy from code-behind,
    /// where the <c>{t:Theme}</c> markup extension is not available.</summary>
    public static readonly BindableProperty ThemeKeyProperty =
        BindableProperty.Create(nameof(ThemeKey), typeof(string), typeof(IconView), "", propertyChanged: Redraw);

    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double StrokeSize { get => (double)GetValue(StrokeSizeProperty); set => SetValue(StrokeSizeProperty, value); }
    public string ThemeKey { get => (string)GetValue(ThemeKeyProperty); set => SetValue(ThemeKeyProperty, value); }

    public IconView()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        WidthRequest = HeightRequest = 24;
        InputTransparent = true;
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += (_, _) => { if (ThemeKey != "") Invalidate(); };
        Services.AppSettings.Changed += (_, key) => { if (key == nameof(Services.AppSettings.Theme) && ThemeKey != "") Invalidate(); };
    }

    private Color EffectiveColor => ThemeKey != "" ? Theme.Tokens.Current(ThemeKey) : Color;

    private static void Redraw(BindableObject b, object o, object n) => ((IconView)b).Invalidate();

    private static void Resize(BindableObject b, object o, object n)
    {
        var v = (IconView)b;
        v.WidthRequest = v.HeightRequest = (double)n;
        v.Invalidate();
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
        float s = Math.Min(rect.Width, rect.Height);
        if (s <= 0) return;
        canvas.SaveState();
        canvas.Translate((rect.Width - s) / 2, (rect.Height - s) / 2);

        if (Name.StartsWith("flag_", StringComparison.OrdinalIgnoreCase))
        {
            DrawFlag(canvas, Name[5..], s);
            canvas.RestoreState();
            return;
        }

        var glyph = Icons.Get(Name);
        if (glyph != null)
        {
            float k = s / 24f;
            canvas.Scale(k, k);
            Icons.Draw(canvas, glyph, EffectiveColor, (float)StrokeSize);
        }
        canvas.RestoreState();
    }

    /// <summary>Flags are drawn 20×14 inside the box, rounded, with their own
    /// colours — they are the only non-tinted icons in the app.</summary>
    private static void DrawFlag(ICanvas canvas, string code, float s)
    {
        float w = s, h = s * 0.7f, y = (s - h) / 2, r = s * 0.15f;
        canvas.SaveState();
        var clip = new PathF();
        clip.AppendRoundedRectangle(0, y, w, h, r);
        canvas.ClipPath(clip);
        switch (code.ToLowerInvariant())
        {
            case "ua":
                canvas.FillColor = Color.FromArgb("#0057B7");
                canvas.FillRectangle(0, y, w, h / 2);
                canvas.FillColor = Color.FromArgb("#FFD700");
                canvas.FillRectangle(0, y + h / 2, w, h / 2);
                break;
            case "gb":
                canvas.FillColor = Color.FromArgb("#012169");
                canvas.FillRectangle(0, y, w, h);
                canvas.StrokeLineCap = LineCap.Butt;
                canvas.StrokeColor = Colors.White; canvas.StrokeSize = s * 0.15f;
                canvas.DrawLine(0, y, w, y + h); canvas.DrawLine(w, y, 0, y + h);
                canvas.StrokeColor = Color.FromArgb("#C8102E"); canvas.StrokeSize = s * 0.06f;
                canvas.DrawLine(0, y, w, y + h); canvas.DrawLine(w, y, 0, y + h);
                canvas.StrokeColor = Colors.White; canvas.StrokeSize = s * 0.2f;
                canvas.DrawLine(w / 2, y, w / 2, y + h); canvas.DrawLine(0, y + h / 2, w, y + h / 2);
                canvas.StrokeColor = Color.FromArgb("#C8102E"); canvas.StrokeSize = s * 0.1f;
                canvas.DrawLine(w / 2, y, w / 2, y + h); canvas.DrawLine(0, y + h / 2, w, y + h / 2);
                break;
        }
        canvas.RestoreState();
    }
}

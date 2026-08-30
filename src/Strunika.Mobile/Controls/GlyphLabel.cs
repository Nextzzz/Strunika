namespace Strunika.Mobile.Controls;

/// <summary>
/// A short piece of text centred on its own ink, not on its line box. A Label
/// centres the box the font reports, and the display serif (Vollkorn) reserves
/// far more room above and below than a chord name uses — so "D" in a chip sat
/// visibly off centre however the Label was aligned. This draws the string and
/// places it by the bounds it actually occupies.
/// </summary>
public sealed class GlyphLabel : GraphicsView, IDrawable
{
    private static void Redraw(BindableObject b, object? o, object? n) => ((GlyphLabel)b).Invalidate();

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(GlyphLabel), "", propertyChanged: Redraw);
    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(GlyphLabel), Colors.White, propertyChanged: Redraw);
    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(GlyphLabel), 14.0, propertyChanged: Redraw);
    public static readonly BindableProperty FontFamilyProperty =
        BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(GlyphLabel), "Display", propertyChanged: Redraw);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public string FontFamily { get => (string)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public GlyphLabel()
    {
        Drawable = this;
        InputTransparent = true;
        HeightRequest = 20;
        WidthRequest = 30;
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Handler == null) return;                                 // torn down: nothing to draw for
        try { DrawCore(canvas, rect); }
        catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
    }

    private void DrawCore(ICanvas canvas, RectF rect)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;
        var font = new Microsoft.Maui.Graphics.Font(FontFamily);
        float size = (float)FontSize;
        // Shrink rather than clip if the caller gave us less room than the text needs.
        float width = canvas.GetStringSize(text, font, size).Width;
        if (width > rect.Width && width > 0) size *= rect.Width / width;
        canvas.Font = font;
        canvas.FontSize = size;
        canvas.FontColor = TextColor;
        canvas.DrawString(text, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
    }

    /// <summary>Enough room for the string at its size, so a chip hugs it.</summary>
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        double w = Math.Max(WidthRequest, Text.Length * FontSize * 0.72 + 4);
        return new Size(Math.Min(w, widthConstraint), Math.Min(Math.Max(HeightRequest, FontSize * 1.2), heightConstraint));
    }
}

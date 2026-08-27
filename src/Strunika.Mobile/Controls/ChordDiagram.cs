using Strunika.Mobile.Models;

namespace Strunika.Mobile.Controls;

/// <summary>
/// Guitar chord box: six strings, five frets, nut or base-fret number, dots,
/// barre bar, X / O markers. Mirrored for left-handed players. Colours come
/// from the page (accent for dots, primary for lines) so it works on both themes.
/// </summary>
public sealed class ChordDiagram : GraphicsView, IDrawable
{
    public static readonly BindableProperty ShapeProperty =
        BindableProperty.Create(nameof(Shape), typeof(ChordShape), typeof(ChordDiagram), null, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(ChordDiagram), Colors.Gray, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty DotColorProperty =
        BindableProperty.Create(nameof(DotColor), typeof(Color), typeof(ChordDiagram), Colors.Goldenrod, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty MutedColorProperty =
        BindableProperty.Create(nameof(MutedColor), typeof(Color), typeof(ChordDiagram), Colors.DarkGray, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(ChordDiagram), "", propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty TitleColorProperty =
        BindableProperty.Create(nameof(TitleColor), typeof(Color), typeof(ChordDiagram), Colors.Goldenrod, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty ShowFretsProperty =
        BindableProperty.Create(nameof(ShowFrets), typeof(bool), typeof(ChordDiagram), false, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty FretTextColorProperty =
        BindableProperty.Create(nameof(FretTextColor), typeof(Color), typeof(ChordDiagram), Colors.Gray, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
    public static readonly BindableProperty LeftHandedProperty =
        BindableProperty.Create(nameof(LeftHanded), typeof(bool), typeof(ChordDiagram), false, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());

    public ChordShape? Shape { get => (ChordShape?)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public Color DotColor { get => (Color)GetValue(DotColorProperty); set => SetValue(DotColorProperty, value); }
    public Color MutedColor { get => (Color)GetValue(MutedColorProperty); set => SetValue(MutedColorProperty, value); }
    public bool LeftHanded { get => (bool)GetValue(LeftHandedProperty); set => SetValue(LeftHandedProperty, value); }
    /// <summary>The chord's name above the box. It is never allowed to be wider
    /// than the box: a long name simply gets a smaller size.</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Color TitleColor { get => (Color)GetValue(TitleColorProperty); set => SetValue(TitleColorProperty, value); }
    /// <summary>Draw the fret number under each string, aligned with it.</summary>
    public bool ShowFrets { get => (bool)GetValue(ShowFretsProperty); set => SetValue(ShowFretsProperty, value); }
    public Color FretTextColor { get => (Color)GetValue(FretTextColorProperty); set => SetValue(FretTextColorProperty, value); }

    public ChordDiagram() => Drawable = this;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var shape = Shape;
        // Fit the drawing to the space with a fixed proportion and centre it:
        // the panel row is a star row, and a HeightRequest here would stop it
        // shrinking when the YouTube player takes its space.
        const float ratio = 0.66f;                               // width : height
        float fitW = Math.Min(rect.Width, rect.Height * ratio);
        float fitH = Math.Min(rect.Height, rect.Width / ratio);
        rect = new RectF(rect.Center.X - fitW / 2, rect.Center.Y - fitH / 2, fitW, fitH);
        string title = Title ?? "";
        // The name gets the top 22 %; with no shape there is no box to sit above,
        // so the same-sized name is centred in the whole control instead.
        float titleRow = title.Length == 0 ? 0f : rect.Height * 0.22f;
        float titleBox = shape == null ? rect.Height : titleRow;
        if (titleRow > 0)
        {
            // Centred over the fret box itself (the control is inset on the left
            // for the base-fret number), and never wider than it.
            float boxLeft = rect.Left + rect.Width * 0.22f, boxRight = rect.Right - rect.Width * 0.06f;
            var font = new Microsoft.Maui.Graphics.Font("DisplayBold");
            float size = titleRow * 0.92f;
            float max = (boxRight - boxLeft) * 1.05f;
            float w = canvas.GetStringSize(title, font, size).Width;
            if (w > max) size *= max / w;
            canvas.Font = font;
            canvas.FontSize = size;
            canvas.FontColor = TitleColor;
            canvas.DrawString(title, boxLeft - 24f, rect.Top, boxRight - boxLeft + 48f, titleBox, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        }
        if (shape == null) return;
        const int strings = 6, frets = 5;
        rect = new RectF(rect.Left, rect.Top + titleRow, rect.Width, rect.Height - titleRow);
        float top = rect.Top + rect.Height * 0.16f;          // room for X / O markers
        float numberRow = ShowFrets ? rect.Height * 0.15f : 0f;
        float bottom = rect.Bottom - rect.Height * 0.04f - numberRow;
        float left = rect.Left + rect.Width * 0.22f;          // gutter for the base-fret number
        float right = rect.Right - rect.Width * 0.06f;
        float sx = (right - left) / (strings - 1);
        float fy = (bottom - top) / frets;
        float X(int stringIndex) => LeftHanded ? right - stringIndex * sx : left + stringIndex * sx;

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 1.2f;
        for (int s = 0; s < strings; s++)
            canvas.DrawLine(X(s), top, X(s), bottom);
        for (int f = 0; f <= frets; f++)
            canvas.DrawLine(left, top + f * fy, right, top + f * fy);
        if (shape.BaseFret == 1)
        {
            canvas.StrokeSize = 4f;
            canvas.DrawLine(left - 0.6f, top, right + 0.6f, top);
        }
        else
        {
            // Outside the grid, in the left gutter, level with the first fret —
            // inside the box it was lost among the strings. The gap grows with
            // the grid, and a two-digit fret is scaled to fit on one line.
            var label = shape.BaseFret.ToString();
            float gap = sx * 0.30f;
            float gutter = left - rect.Left - gap;
            float size = Math.Max(8f, fy * 0.62f);
            float textWidth = canvas.GetStringSize(label, Microsoft.Maui.Graphics.Font.Default, size).Width;
            if (textWidth > gutter) size *= gutter / textWidth;
            canvas.FontColor = TitleColor;
            canvas.FontSize = size;
            canvas.DrawString(label, rect.Left, top + fy * 0.5f - size, gutter, size * 2, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        float dotR = Math.Min(sx, fy) * 0.34f;
        // Barre: a bar across all strings fretted at the barre fret.
        if (shape.Barre > 0)
        {
            int first = -1, last = -1;
            for (int s = 0; s < strings; s++)
                if (shape.Frets[s] == shape.Barre) { if (first < 0) first = s; last = s; }
            if (first >= 0 && last > first)
            {
                int row = shape.Barre - shape.BaseFret;
                float y = top + row * fy + fy / 2;
                float x0 = Math.Min(X(first), X(last)) - dotR, x1 = Math.Max(X(first), X(last)) + dotR;
                canvas.FillColor = DotColor;
                canvas.FillRoundedRectangle(x0, y - dotR * 0.8f, x1 - x0, dotR * 1.6f, dotR * 0.8f);
            }
        }
        for (int s = 0; s < strings; s++)
        {
            int fret = shape.Frets[s];
            float x = X(s);
            if (fret < 0)
            {
                canvas.StrokeColor = MutedColor;
                canvas.StrokeSize = 1.3f;
                float m = sx * 0.20f, my = top - rect.Height * 0.085f;
                canvas.DrawLine(x - m, my - m, x + m, my + m);
                canvas.DrawLine(x - m, my + m, x + m, my - m);
            }
            else if (fret == 0)
            {
                canvas.StrokeColor = LineColor;
                canvas.StrokeSize = 1.3f;
                canvas.DrawCircle(x, top - rect.Height * 0.085f, sx * 0.19f);
            }
            else if (fret != shape.Barre)
            {
                int row = fret - shape.BaseFret;
                canvas.FillColor = DotColor;
                canvas.FillCircle(x, top + row * fy + fy / 2, dotR);
            }
        }

        // Fret numbers, each centred on its own string.
        if (ShowFrets)
        {
            canvas.FontColor = FretTextColor;
            canvas.FontSize = numberRow * 0.74f;
            for (int s = 0; s < strings; s++)
            {
                int fret = shape.Frets[s];
                canvas.DrawString(fret < 0 ? "x" : fret.ToString(), X(s) - sx / 2, bottom + numberRow * 0.16f, sx, numberRow,
                                  HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}

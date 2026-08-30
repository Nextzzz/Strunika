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
    public static readonly BindableProperty CapoProperty =
        BindableProperty.Create(nameof(Capo), typeof(int), typeof(ChordDiagram), 0, propertyChanged: (b, _, _) => ((ChordDiagram)b).Invalidate());
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
    /// <summary>Capo fret, 0 for none. The box's nut is then the capo, so the
    /// number in the gutter counts from the real nut: a player has to know
    /// where on the neck the shape sits.</summary>
    public int Capo { get => (int)GetValue(CapoProperty); set => SetValue(CapoProperty, value); }
    /// <summary>Draw the fret number under each string, aligned with it.</summary>
    public bool ShowFrets { get => (bool)GetValue(ShowFretsProperty); set => SetValue(ShowFretsProperty, value); }
    public Color FretTextColor { get => (Color)GetValue(FretTextColorProperty); set => SetValue(FretTextColorProperty, value); }

    /// <summary>Side margin of the fret box, as a fraction of the width.</summary>
    private const float Gutter = 0.15f;

    public ChordDiagram() => Drawable = this;

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
            // Centred over the fret box, which is itself centred in the control.
            float boxLeft = rect.Left + rect.Width * Gutter, boxRight = rect.Right - rect.Width * Gutter;
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
        // Equal margins: the grid is the middle of the diagram, so a caption or a
        // button centred under the control lines up with it. The left margin also
        // carries the fret number, which is scaled to fit it.
        float left = rect.Left + rect.Width * Gutter;
        float right = rect.Right - rect.Width * Gutter;
        float sx = (right - left) / (strings - 1);
        float fy = (bottom - top) / frets;
        float X(int stringIndex) => LeftHanded ? right - stringIndex * sx : left + stringIndex * sx;

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 1.2f;
        for (int s = 0; s < strings; s++)
            canvas.DrawLine(X(s), top, X(s), bottom);
        for (int f = 0; f <= frets; f++)
            canvas.DrawLine(left, top + f * fy, right, top + f * fy);
        // The nut (or, with a capo on, the capo itself).
        if (shape.BaseFret == 1)
        {
            canvas.StrokeSize = 4f;
            canvas.DrawLine(left - 0.6f, top, right + 0.6f, top);
        }
        // What the number means depends on the shape: a barre chord is placed by
        // its barre, a shape up the neck by its first row (standard notation), and
        // an open shape under a capo by the capo itself, level with the top line.
        // A barre on the first fret with no capo needs no number at all.
        int barreFret = shape.Barre > 0 ? shape.Barre + Capo : 0;
        bool atCapo = shape.Barre == 0 && shape.BaseFret == 1;
        if (barreFret > 1 || (shape.Barre == 0 && (shape.BaseFret > 1 || Capo > 0)))
        {
            // Outside the grid, in the left gutter, level with the first fret —
            // inside the box it was lost among the strings. The gap grows with
            // the grid, and a two-digit fret is scaled to fit on one line.
            var label = barreFret > 0 ? barreFret.ToString()
                      : atCapo ? Capo.ToString()
                      : (shape.BaseFret + Capo).ToString();
            // Air between the number and the grid, so it never crowds the nut.
            float gap = sx * 0.24f + 3f;
            float gutter = Math.Max(6f, left - rect.Left - gap);
            float size = Math.Max(7f, fy * 0.62f);
            // Two digits get the same treatment as the numbers under the strings:
            // shrink to the room there is, never wrap onto a second line.
            float textWidth = canvas.GetStringSize(label, Microsoft.Maui.Graphics.Font.Default, size).Width;
            if (textWidth > gutter) size = Math.Max(7f, size * gutter / textWidth);
            canvas.FontColor = TitleColor;
            canvas.FontSize = size;
            float barreRow = shape.Barre > 0 ? shape.Barre - shape.BaseFret : 0;
            float labelY = atCapo ? top - size : top + (barreRow + 0.5f) * fy - size;
            // Right-aligned in a box that starts well left of the control: the text
            // is placed by its right edge and can never be wrapped by the width.
            const float overflow = 40f;
            canvas.DrawString(label, rect.Left - overflow, labelY, gutter + overflow, size * 2, HorizontalAlignment.Right, VerticalAlignment.Center);
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
                // Just past the outer strings it sits on — a full dot radius on each
                // side made the bar hang well outside the box.
                float overhang = dotR * 0.5f;
                float x0 = Math.Min(X(first), X(last)) - overhang, x1 = Math.Max(X(first), X(last)) + overhang;
                canvas.FillColor = DotColor;
                canvas.FillRoundedRectangle(x0, y - dotR * 0.72f, x1 - x0, dotR * 1.44f, dotR * 0.72f);
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

        // Fret numbers, each centred on its own string. They name frets on the real
        // neck, so the capo counts: a string left open is played at the capo, and a
        // fretted one is that many frets above it.
        if (ShowFrets)
        {
            var numbers = new string[strings];
            for (int s = 0; s < strings; s++)
            {
                int fret = shape.Frets[s];
                numbers[s] = fret < 0 ? "x" : (fret == 0 ? Capo : fret + Capo).ToString();
            }
            // Two digits must fit between the strings *with a gap*: six numbers
            // touching each other read as one long number ("101212101010").
            float size = numberRow * 0.74f;
            float room = sx * 0.62f;
            foreach (var text in numbers)
            {
                float w = canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.Default, size).Width;
                if (w > room) size *= room / w;
            }
            size = Math.Max(size, numberRow * 0.34f);            // never so small it stops being readable
            canvas.FontColor = FretTextColor;
            canvas.FontSize = size;
            for (int s = 0; s < strings; s++)
                canvas.DrawString(numbers[s], X(s) - sx / 2, bottom + numberRow * 0.16f, sx, numberRow,
                                  HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}

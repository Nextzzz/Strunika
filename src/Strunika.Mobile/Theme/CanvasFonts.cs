namespace Strunika.Mobile.Theme;

/// <summary>
/// A registered font for a canvas. MAUI knows "Display" and "DisplayBold" as
/// aliases from MauiProgram; Maui.Graphics on iOS asks Core Text for a font
/// by name, and Core Text knows only the real one (Vollkorn-Bold), so a
/// canvas asked for the alias drew its text with nothing — the chord names
/// over the diagrams were missing on the phone. The alias is resolved once
/// through the font manager; where that cannot happen the alias is used as
/// it is (the Windows head resolves it itself).
/// </summary>
public static class CanvasFonts
{
    private static readonly Dictionary<string, string> Names = new();

    /// <summary>
    /// Core Text (iOS) lays a line only into a box the whole line fits and
    /// draws nothing otherwise, while DirectWrite (Windows) draws and clips —
    /// which is why text that was fine on the Windows head vanished on the
    /// phone. No font metric is guessed here: the line is measured with the
    /// canvas itself (<c>GetStringSize</c> reports the line height the layout
    /// will use), and every draw goes through <see cref="Draw"/>, which
    /// gives the line a box it fits, or <see cref="FitHeight"/>, which sizes
    /// the text so its line fits the box with room to spare.
    /// </summary>
    private static bool _logged;

    /// <summary>The size at which <paramref name="text"/>'s line takes at most
    /// <paramref name="fill"/> of <paramref name="boxHeight"/> — starting from
    /// <paramref name="size"/> and only ever going down.</summary>
    public static float FitHeight(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font, float size, float boxHeight, float fill = 0.86f)
    {
        if (size <= 0 || boxHeight <= 0 || string.IsNullOrEmpty(text)) return size;
        float line = canvas.GetStringSize(text, font, size).Height;
        if (line <= 0) return size;
        float room = boxHeight * fill;
        if (line > room) size *= room / line;
        if (!_logged)
        {
            _logged = true;
            Strunika.Core.Diagnostics.FileLog.Info($"canvas text: '{text}' {font.Name} {size:0.0} pt → line {canvas.GetStringSize(text, font, size).Height:0.0} in box {boxHeight:0.0}");
        }
        return size;
    }

    /// <summary>Draw <paramref name="text"/> centred on <paramref name="box"/>.
    /// The frame handed to the canvas is the box or the measured line,
    /// whichever is taller, centred on the box: a box shorter than the line
    /// (a chip sized to the glyph, not to the face's line) would otherwise
    /// draw nothing at all.</summary>
    public static void Draw(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font, float size, RectF box,
                            HorizontalAlignment horizontal = HorizontalAlignment.Center)
    {
        if (string.IsNullOrEmpty(text) || size <= 0 || box.Width <= 0) return;
        float line = canvas.GetStringSize(text, font, size).Height;
        float tall = Math.Max(box.Height, line + 2f);
        canvas.Font = font;
        canvas.FontSize = size;
        canvas.DrawString(text, box.X, box.Y + (box.Height - tall) / 2, box.Width, tall, horizontal, VerticalAlignment.Center);
    }

    public static Microsoft.Maui.Graphics.Font Named(string alias)
    {
        if (!Names.TryGetValue(alias, out var name))
        {
            name = alias;
#if IOS
            try
            {
                var manager = Application.Current?.Handler?.MauiContext?.Services.GetService<IFontManager>();
                var font = manager?.GetFont(Microsoft.Maui.Font.OfSize(alias, 17, FontWeight.Regular, FontSlant.Default, true));
                if (font != null && !string.IsNullOrEmpty(font.Name)) name = font.Name;
            }
            catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("canvas font " + alias, ex); }
            if (name == alias) return new Microsoft.Maui.Graphics.Font(alias);   // not resolvable yet: try again next time
#endif
            Names[alias] = name;
        }
        return new Microsoft.Maui.Graphics.Font(name);
    }
}

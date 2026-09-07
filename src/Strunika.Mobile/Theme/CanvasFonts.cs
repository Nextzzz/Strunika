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

    /// <summary>Draw <paramref name="text"/> with its capitals centred on
    /// <paramref name="box"/>. On iOS the canvas's own "centre" centres the
    /// line box and then nudges it up by half the descent, which for a face
    /// with tall ascenders puts the letters well above the middle; so the
    /// text is laid from the top of a frame placed by the face's real ascent
    /// and cap height (Core Text puts the first baseline one ascent below the
    /// frame's top), and the frame is the line's height, so it always fits.
    /// Elsewhere the canvas centres the line box, which is close enough.</summary>
    public static void Draw(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font, float size, RectF box,
                            HorizontalAlignment horizontal = HorizontalAlignment.Center)
    {
        if (string.IsNullOrEmpty(text) || size <= 0 || box.Width <= 0) return;
        canvas.Font = font;
        canvas.FontSize = size;
#if IOS
        var (ascent, descent, cap) = MetricsOf(font, size);
        float cy = box.Y + box.Height / 2;
        float top = cy - ascent + cap / 2;
        canvas.DrawString(text, box.X, top, box.Width, ascent + descent + 2f, horizontal, VerticalAlignment.Top);
#else
        float line = canvas.GetStringSize(text, font, size).Height;
        float tall = Math.Max(box.Height, line + 2f);
        canvas.DrawString(text, box.X, box.Y + (box.Height - tall) / 2, box.Width, tall, horizontal, VerticalAlignment.Center);
#endif
    }

#if IOS
    private static readonly Dictionary<(string, int), (float Ascent, float Descent, float Cap)> Metrics = new();

    /// <summary>Ascent, descent and cap height of the face at this size, from
    /// Core Text — the same font the canvas will draw with.</summary>
    private static (float Ascent, float Descent, float Cap) MetricsOf(Microsoft.Maui.Graphics.Font font, float size)
    {
        var key = (font.Name ?? (font.Weight >= 600 ? "<bold>" : "<system>"), (int)Math.Round(size * 4));
        if (Metrics.TryGetValue(key, out var known)) return known;
        (float, float, float) metrics;
        try
        {
            // UIFont and Core Text read the same tables; a registered face is
            // found by its real name, the system face by weight.
            var ui = (string.IsNullOrEmpty(font.Name) ? null : UIKit.UIFont.FromName(font.Name, size))
                     ?? UIKit.UIFont.SystemFontOfSize(size, font.Weight >= 600 ? UIKit.UIFontWeight.Bold : UIKit.UIFontWeight.Regular);
            metrics = ((float)ui.Ascender, (float)Math.Abs(ui.Descender), (float)ui.CapHeight);
        }
        catch (Exception ex)
        {
            Strunika.Core.Diagnostics.FileLog.Error("font metrics " + key.Item1, ex);
            metrics = (size * 0.95f, size * 0.25f, size * 0.7f);
        }
        Metrics[key] = metrics;
        return metrics;
    }
#endif

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

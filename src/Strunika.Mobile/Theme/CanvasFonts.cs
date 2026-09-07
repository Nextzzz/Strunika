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
    /// Line height over font size, the number a canvas box must be sized by.
    /// Core Text (iOS) lays a line only into a box the whole line fits, and
    /// draws nothing otherwise, while DirectWrite (Windows) draws and clips —
    /// so every DrawString box has to be at least the line: 1.3 × size for
    /// Vollkorn (the display face, tall ascenders), 1.25 × size for the system
    /// face. Size text from its box with these, never the box from the text.
    /// </summary>
    public const float DisplayLine = 1.3f, SystemLine = 1.25f;

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

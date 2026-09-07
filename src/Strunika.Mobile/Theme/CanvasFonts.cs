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

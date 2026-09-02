namespace Strunika.Mobile.Theme;

/// <summary>
/// Brand tokens — the single source of truth for themed colours in code
/// (see .claude/skills/strunika-ui/SKILL.md §1). XAML reaches them through
/// <c>{t:Theme Key}</c>; Colors.xaml only mirrors the raw brand colours.
/// </summary>
public static class Tokens
{
    public static readonly Color Gold = Color.FromArgb("#D9AC4C");
    public static readonly Color Copper = Color.FromArgb("#AE6F32");
    public static readonly Color Cream = Color.FromArgb("#E9D3A2");
    public static readonly Color DarkBase = Color.FromArgb("#16110B");
    public static readonly Color LightBase = Color.FromArgb("#FBF3E3");

    private static readonly Dictionary<string, (Color Light, Color Dark)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bg"] = (Color.FromArgb("#FBF3E3"), Color.FromArgb("#16110B")),
        ["Surface1"] = (Color.FromArgb("#F3E8CF"), Color.FromArgb("#211A10")),
        ["Surface2"] = (Color.FromArgb("#E9D3A2"), Color.FromArgb("#2C2316")),
        ["Separator"] = (Color.FromArgb("#D9C6A0"), Color.FromArgb("#3A2E1C")),
        ["TextPri"] = (Color.FromArgb("#16110B"), Color.FromArgb("#E9D3A2")),
        ["TextSec"] = (Color.FromArgb("#66522F"), Color.FromArgb("#A48F66")),
        ["Dim"] = (Color.FromArgb("#A08A5C"), Color.FromArgb("#7A6543")),
        ["Accent"] = (Color.FromArgb("#AE6F32"), Color.FromArgb("#D9AC4C")),
        ["AccentText"] = (Color.FromArgb("#AE6F32"), Color.FromArgb("#D9AC4C")),
        ["Accent2"] = (Color.FromArgb("#D9AC4C"), Color.FromArgb("#AE6F32")),
        ["Fill"] = (Color.FromArgb("#AE6F32"), Color.FromArgb("#D9AC4C")),
        ["OnFill"] = (Color.FromArgb("#FFF8EC"), Color.FromArgb("#16110B")),
        // One rule for both themes (2026-09-02): the guitar's colour is the accent —
        // Gold on dark, Copper on light — and everything the guitar's colour paints
        // on dark (Pro, the string, filled buttons) takes the guitar's colour on light
        // too. Text on that fill is ink on gold, cream on copper.
        ["OnAccent"] = (Color.FromArgb("#FFF8EC"), Color.FromArgb("#16110B")),
        ["Glow"] = (Color.FromArgb("#4DAE6F32"), Color.FromArgb("#73D9AC4C")),
        ["Error"] = (Color.FromArgb("#A8402C"), Color.FromArgb("#C4533A")),
    };

    public static IEnumerable<string> Keys => Map.Keys;

    public static Color Light(string key) => Map.TryGetValue(key, out var c) ? c.Light : Colors.Magenta;

    public static Color Dark(string key) => Map.TryGetValue(key, out var c) ? c.Dark : Colors.Magenta;

    /// <summary>Colour for the theme currently in effect (user override or system).</summary>
    public static Color Current(string key)
    {
        var app = Application.Current;
        var theme = app == null ? AppTheme.Dark
            : app.UserAppTheme != AppTheme.Unspecified ? app.UserAppTheme : app.RequestedTheme;
        return theme == AppTheme.Light ? Light(key) : Dark(key);
    }
}

namespace Strunika.Mobile.Controls;

/// <summary>
/// Stroke icons on a 24-unit grid (1.8 stroke, round caps), the same set
/// the mockups use. Kept as SVG path data so <see cref="IconView"/> and
/// <see cref="PillTabBar"/> draw them with Microsoft.Maui.Graphics and
/// tint them per theme — no PNG variants, no tint behaviours.
/// </summary>
public static class Icons
{
    public sealed record Glyph(string[] Paths, (float X, float Y, float R)[] Circles, string[]? FilledPaths = null);

    private static readonly Dictionary<string, Glyph> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fork"] = new(new[] { "M8 3v7a4 4 0 0 0 8 0V3", "M12 14v7", "M9.5 21h5" }, Array.Empty<(float, float, float)>()),
        ["wave"] = new(new[] { "M2 12h2.5l2-5 3 10 3-14 3 12 2-6 1.5 3H22" }, Array.Empty<(float, float, float)>()),
        ["songs"] = new(new[] { "M9 18V7l11-3v11" }, new[] { (6f, 18f, 3f), (17f, 15f, 3f) }),
        ["sliders"] = new(new[] { "M4 7h9M17 7h3M4 17h3M11 17h9" }, new[] { (15f, 7f, 2.2f), (9f, 17f, 2.2f) }),
        ["gear"] = new(new[] { "M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1" }, new[] { (12f, 12f, 3.2f) }),
        ["plus"] = new(new[] { "M12 5v14M5 12h14" }, Array.Empty<(float, float, float)>()),
        ["lock"] = new(new[] { "M5 11h14v10H5z", "M8 11V7a4 4 0 0 1 8 0v4" }, Array.Empty<(float, float, float)>()),
        ["search"] = new(new[] { "M16 16l5 5" }, new[] { (11f, 11f, 6.5f) }),
        ["star"] = new(new[] { "M12 3.5l2.6 5.6 6.1.7-4.5 4.2 1.2 6-5.4-3-5.4 3 1.2-6L3.3 9.8l6.1-.7z" }, Array.Empty<(float, float, float)>()),
        ["chevL"] = new(new[] { "M15 5l-7 7 7 7" }, Array.Empty<(float, float, float)>()),
        ["chevR"] = new(new[] { "M9 5l7 7-7 7" }, Array.Empty<(float, float, float)>()),
        ["chevD"] = new(new[] { "M6 9l6 6 6-6" }, Array.Empty<(float, float, float)>()),
        ["metro"] = new(new[] { "M9.5 3.5h5L18 20H6z", "M12 15l5.5-9" }, Array.Empty<(float, float, float)>()),
        ["mic"] = new(new[] { "M9 6a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0z", "M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21M9 21h6" }, Array.Empty<(float, float, float)>()),
        ["pencil"] = new(new[] { "M4 20l4-1L19 8l-3-3L5 16z", "M14 7l3 3" }, Array.Empty<(float, float, float)>()),
        ["back5"] = new(new[] { "M11 6l-6 6 6 6M19 6l-6 6 6 6" }, Array.Empty<(float, float, float)>()),
        ["fwd5"] = new(new[] { "M13 6l6 6-6 6M5 6l6 6-6 6" }, Array.Empty<(float, float, float)>()),
        ["folder"] = new(new[] { "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" }, Array.Empty<(float, float, float)>()),
        ["youtube"] = new(new[] { "M7 6h10a4 4 0 0 1 4 4v4a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4v-4a4 4 0 0 1 4-4z" }, Array.Empty<(float, float, float)>(), new[] { "M10 9.5v5l4.5-2.5z" }),
        ["file"] = new(new[] { "M6 3h8l4 4v14H6z", "M14 3v4h4" }, Array.Empty<(float, float, float)>()),
        ["x"] = new(new[] { "M6 6l12 12M18 6L6 18" }, Array.Empty<(float, float, float)>()),
        ["check"] = new(new[] { "M5 12l4.5 4.5L19 7" }, Array.Empty<(float, float, float)>()),
        ["undo"] = new(new[] { "M9 14l-4-4 4-4", "M5 10h9a5 5 0 0 1 0 10h-2" }, Array.Empty<(float, float, float)>()),
        ["redo"] = new(new[] { "M15 14l4-4-4-4", "M19 10h-9a5 5 0 0 0 0 10h2" }, Array.Empty<(float, float, float)>()),
        ["loop"] = new(new[] { "M17 4l3 3-3 3", "M20 7H8a4 4 0 0 0-4 4v1", "M7 20l-3-3 3-3", "M4 17h12a4 4 0 0 0 4-4v-1" }, Array.Empty<(float, float, float)>()),
        ["play"] = new(Array.Empty<string>(), Array.Empty<(float, float, float)>(), new[] { "M8 5v14l11-7z" }),
        ["auto"] = new(Array.Empty<string>(), new[] { (12f, 12f, 8.5f) }, new[] { "M12 3.5v17A8.5 8.5 0 0 0 12 3.5z" }),
        ["sun"] = new(new[] { "M12 2.5v2.5M12 19v2.5M2.5 12H5M19 12h2.5M5.3 5.3l1.8 1.8M16.9 16.9l1.8 1.8M5.3 18.7l1.8-1.8M16.9 7.1l1.8-1.8" }, new[] { (12f, 12f, 4f) }),
        ["moon"] = new(new[] { "M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z" }, Array.Empty<(float, float, float)>()),
    };

    public static Glyph? Get(string name) => All.TryGetValue(name, out var g) ? g : null;

    /// <summary>Draws a glyph into a 24×24 box at the canvas origin.</summary>
    public static void Draw(ICanvas canvas, Glyph glyph, Color color, float strokeSize = 1.8f)
    {
        canvas.StrokeColor = color;
        canvas.StrokeSize = strokeSize;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.FillColor = color;
        foreach (var d in glyph.Paths)
            canvas.DrawPath(PathBuilder.Build(d));
        foreach (var (x, y, r) in glyph.Circles)
            canvas.DrawCircle(x, y, r);
        if (glyph.FilledPaths != null)
            foreach (var d in glyph.FilledPaths)
                canvas.FillPath(PathBuilder.Build(d));
    }
}

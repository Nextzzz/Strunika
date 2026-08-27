namespace Strunika.Mobile.Services;

/// <summary>
/// Device-shaped windows for the Windows dev head, so every screen can be
/// checked from iPhone SE to iPad Pro without a device. Chosen from
/// Settings → About (debug builds) and applied live; the choice is remembered.
/// <c>STRUNIKA_WINDOW=WxH</c> (the launch profiles) still wins when set.
/// </summary>
public static class DevWindow
{
    public sealed record Preset(string Name, double Width, double Height);

    public static readonly IReadOnlyList<Preset> Presets = new[]
    {
        new Preset("iPhone SE", 375, 667),
        new Preset("iPhone 13 mini", 375, 812),
        new Preset("iPhone 16", 393, 852),
        new Preset("iPhone 16 Pro Max", 430, 932),
        new Preset("iPad mini", 744, 1133),
        new Preset("iPad 11 in", 820, 1180),
        new Preset("iPad Pro 13 in", 1024, 1366),
        new Preset("iPhone 16 · landscape", 852, 393),
        new Preset("iPad 11 in · landscape", 1180, 820),
    };

    public static Preset Default => Presets[3];

#if WINDOWS && DEBUG
    public static bool IsAvailable => true;
#else
    public static bool IsAvailable => false;
#endif

    /// <summary>The remembered preset, or the default.</summary>
    public static Preset Current
    {
        get
        {
            var name = Preferences.Default.Get("dev_window", "");
            return Presets.FirstOrDefault(p => p.Name == name) ?? Default;
        }
    }

    /// <summary>What the window should open at: the env var, then the saved preset.</summary>
    public static (double W, double H) Startup()
    {
        var spec = Environment.GetEnvironmentVariable("STRUNIKA_WINDOW");
        if (!string.IsNullOrWhiteSpace(spec))
        {
            var parts = spec.ToLowerInvariant().Split(new[] { 'x', '×' }, 2);
            if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h) && w > 100 && h > 100)
                return (w, h);
        }
        var p = Current;
        return (p.Width, p.Height);
    }

    /// <summary>Resize the running window to a preset and remember it.</summary>
    public static void Apply(Preset preset)
    {
        Preferences.Default.Set("dev_window", preset.Name);
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window == null) return;
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double screenHeight = display.Height / Math.Max(1, display.Density);
        window.Width = preset.Width;
        window.Height = Math.Min(preset.Height, screenHeight - 48);
    }
}

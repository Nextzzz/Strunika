using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Strunika.Mobile.Theme;

/// <summary>Screen size class, decided by the shortest side of the window so
/// that rotating a phone does not change it (Android's "smallest width").</summary>
public enum WidthClass
{
    /// <summary>Under 380 pt: iPhone SE, 12/13 mini.</summary>
    Compact,
    /// <summary>380–599 pt: every other iPhone.</summary>
    Regular,
    /// <summary>600 pt and up: iPad.</summary>
    Wide,
}

/// <summary>
/// The adaptive-size source of truth. Points are a physical unit, so a 60 pt
/// button is 60 pt on every device; this class supplies the factors that make
/// chrome a little smaller on compact phones and lets hero content grow on a
/// tablet — while chrome deliberately does <b>not</b> grow there (a tablet gets
/// room, not bigger buttons). XAML reaches it through <c>{t:Size}</c>,
/// <c>{t:Round}</c> and <c>{t:ContentInset}</c>; code through
/// <see cref="Size"/>. Rules live in <c>.claude/skills/strunika-ui/SKILL.md</c> §6.
/// </summary>
public sealed class Metrics : INotifyPropertyChanged
{
    public static Metrics Instance { get; } = new();

    /// <summary>The design reference: iPhone 15/16 (393 pt) up to Pro Max (430 pt).</summary>
    public const double CompactBelow = 380, WideFrom = 600;

    public WidthClass Class { get; private set; } = WidthClass.Regular;

    /// <summary>Factor for chrome: buttons, chips, icons, thumbnails.</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>Factor for hero content: the chord, its diagram, the tuner note.</summary>
    public double HeroScale { get; private set; } = 1.0;

    /// <summary>Widest a single content column may be (672 pt, the readable width Apple
    /// uses on iPad); unbounded on phones.</summary>
    public double ContentMaxWidth { get; private set; } = double.PositiveInfinity;

    /// <summary>Side margin that centres a <see cref="ContentMaxWidth"/> column in
    /// the window; zero on phones. <see cref="ContentInsetPlus"/> adds the usual
    /// 20 pt page inset on top, for cards that used to carry it themselves. (A Center alignment would let the column take
    /// its natural width and overflow a narrow screen — this never does.)</summary>
    public Thickness ContentInset { get; private set; }
    public Thickness ContentInsetPlus { get; private set; } = new(20, 0);

    public double ShortestSide { get; private set; }
    public double Width { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A chrome size for code-behind: <c>Metrics.Instance.Size(104)</c>.</summary>
    public double Size(double value, double min = 0, bool hero = false) => Math.Max(min, value * (hero ? HeroScale : Scale));

    /// <summary>Follow the window: rotation, iPad Split View, a resized dev window.</summary>
    public void Attach(Window window)
    {
        window.SizeChanged += (_, _) => Update(window.Width, window.Height);
        DeviceDisplay.Current.MainDisplayInfoChanged += (_, _) => Update(window.Width, window.Height);
        Update(window.Width, window.Height);
    }

    public void Update(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            // Mobile heads may not report window bounds: fall back to the display.
            var d = DeviceDisplay.Current.MainDisplayInfo;
            double density = Math.Max(1, d.Density);
            width = d.Width / density;
            height = d.Height / density;
        }
        double shortest = Math.Min(width, height);
        if (shortest <= 0) return;
        bool sameWidth = Math.Abs(width - Width) < 0.5, sameShortest = Math.Abs(shortest - ShortestSide) < 0.5;
        if (sameWidth && sameShortest) return;
        Width = width;
        ShortestSide = shortest;

        var cls = shortest < CompactBelow ? WidthClass.Compact : shortest < WideFrom ? WidthClass.Regular : WidthClass.Wide;
        var (scale, hero, max) = cls switch
        {
            WidthClass.Compact => (0.88, 0.85, double.PositiveInfinity),
            WidthClass.Wide => (1.0, 1.25, 672.0),
            _ => (1.0, 1.0, double.PositiveInfinity),
        };
        bool changed = cls != Class;
        Class = cls;
        Scale = scale;
        HeroScale = hero;
        ContentMaxWidth = max;
        double side = double.IsFinite(max) ? Math.Max(0, (width - max) / 2) : 0;
        ContentInset = new Thickness(side, 0);
        ContentInsetPlus = new Thickness(side + 20, 0);
        Strunika.Core.Diagnostics.FileLog.Info($"metrics: {width:0}×{height:0} pt → {cls} (scale {scale}, hero {hero}, inset {side:0})");
        Raise(nameof(ShortestSide));
        Raise(nameof(Width));
        Raise(nameof(ContentInset));
        Raise(nameof(ContentInsetPlus));
        if (!changed) return;
        Raise(nameof(Class));
        Raise(nameof(Scale));
        Raise(nameof(HeroScale));
        Raise(nameof(ContentMaxWidth));
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

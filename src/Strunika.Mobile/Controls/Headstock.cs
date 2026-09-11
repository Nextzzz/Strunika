using System.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The tuner's strings with their pegs, laid out as a fir tree across the whole
/// width: the strings stand side by side, all parallel and straight up, and
/// each one's peg sits at its top — the outer strings' pegs lowest, the middle
/// ones' highest, the two halves mirror images (user design 2026-09-11, second
/// round). The strings run on down under the tab bar.
/// <para>
/// A string wears its peg's colour. The one being tuned shakes while it
/// sounds, along all of its length and at its own thickness, pinned at its peg
/// and out of sight behind the bar: wider the harder it is played but never too
/// little to see, dying away with the note, beating while out of tune and
/// steady once in.
/// </para>
/// <para>
/// Per-frame rule (strunika-ui §7): this canvas is redrawn every frame while a
/// string moves, and only then — the same exception as the tuner's own string.
/// A bowed copy swung by a transform was tried first: a transform scales a
/// line's thickness along with its swing, and the part of the string it did not
/// cover stood still beside it and looked wider (user report 2026-09-11).
/// </para>
/// </summary>
public sealed class Headstock : GraphicsView, IDrawable
{
    // Proportions of the peg, which is measured: the pegs are {t:Size} tokens.
    /// <summary>How far each step of the tree climbs: as tall as the room allows.</summary>
    private const double MinRise = 0.35, MaxRise = 0.9;
    /// <summary>String in view under the lowest pegs, before the tab bar's room:
    /// generous when there is space, tight when there is not.</summary>
    private const double RoomyRun = 0.6, TightRun = 0.35;
    /// <summary>String thickness, lowest string to highest, as on a guitar.</summary>
    private const double ThickGauge = 0.065, ThinGauge = 0.032;
    /// <summary>How far the middle of a sounding string swings at its widest.</summary>
    private const double BowRatio = 0.16;
    /// <summary>Where the swinging part of a string ends: half-way down the room
    /// kept for the tab bar, behind the bar itself, so no still end is ever seen.</summary>
    private const double NodeDepth = 0.5;
    /// <summary>Straight pieces a swinging string is drawn with.</summary>
    private const int Steps = 28;

    // Motion.
    /// <summary>Swings per second, lowest string to highest — slow enough to
    /// follow each one (user request 2026-09-11).</summary>
    private const double LowRate = 4.5, HighRate = 7;
    /// <summary>A sounding string always swings at least this share of its
    /// widest, so a soft note visibly moves it too; loudness adds the rest.</summary>
    private const double MinSwing = 0.35;
    /// <summary>Seconds: how fast a swing grows, settles and a twang dies away.</summary>
    private const double Attack = 0.05, Release = 0.35, KickFade = 0.6;
    /// <summary>Beats per second at 50 cents out of tune.</summary>
    private const double MaxBeat = 1.5;
    private const double Still = 0.01;
    private const string Ticker = "headstock-swing";
    private const double Turn = 2 * Math.PI;

    public static readonly BindableProperty IdleColorProperty = Colour(nameof(IdleColor), Colors.Gray);
    public static readonly BindableProperty ActiveColorProperty = Colour(nameof(ActiveColor), Colors.Goldenrod);
    public static readonly BindableProperty TunedColorProperty = Colour(nameof(TunedColor), Colors.Gold);

    private static BindableProperty Colour(string name, Color fallback) =>
        BindableProperty.Create(name, typeof(Color), typeof(Headstock), fallback,
            propertyChanged: (b, _, _) => ((Headstock)b).Invalidate());

    /// <summary>A string whose peg is neither being tuned nor done.</summary>
    public Color IdleColor { get => (Color)GetValue(IdleColorProperty); set => SetValue(IdleColorProperty, value); }
    /// <summary>The string being tuned: its peg's ring.</summary>
    public Color ActiveColor { get => (Color)GetValue(ActiveColorProperty); set => SetValue(ActiveColorProperty, value); }
    /// <summary>A string marked tuned: its peg's fill.</summary>
    public Color TunedColor { get => (Color)GetValue(TunedColorProperty); set => SetValue(TunedColorProperty, value); }

    /// <summary>One string: where it stands, where its peg is, where its
    /// swinging part ends, and how thick it is.</summary>
    private readonly record struct Line(float X, float Peg, float Node, float Gauge);

    /// <summary>How one string is moving.</summary>
    private sealed class Vibration
    {
        public double Amplitude, Target, Kick, Phase, Rate, Beat, BeatPhase;
        /// <summary>Points the middle of the string is off its line this frame.</summary>
        public double Offset;
    }

    private Line[] _lines = Array.Empty<Line>();
    private Vibration[] _vibrations = Array.Empty<Vibration>();
    private (bool Active, bool Tuned)[] _states = Array.Empty<(bool, bool)>();
    private double _bow;
    private bool _ticking;
    private readonly Stopwatch _clock = new();

    public Headstock()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Unloaded += (_, _) => StopTicking();
    }

    /// <summary>The height the pegs of <paramref name="count"/> strings need
    /// above the tab bar's room: with the tree as flat as it may be (tight), or
    /// as tall as it would like (roomy).</summary>
    public static double HeightFor(double peg, int count, bool roomy)
    {
        int rows = (count + 1) / 2;
        return peg + Math.Max(0, rows - 1) * peg * (roomy ? MaxRise : MinRise) + peg * (roomy ? RoomyRun : TightRun);
    }

    /// <summary>
    /// Stands the strings across the width and puts each peg at the top of its
    /// string. <paramref name="below"/> is how far this control reaches under
    /// the content — the room kept for the tab bar: the strings go on into it.
    /// </summary>
    public void Arrange(IReadOnlyList<View> pegs, double below)
    {
        double w = Width, h = Height;
        int n = pegs.Count;
        if (w <= 0 || h <= 0 || n == 0) return;
        double d = pegs[0].Width > 0 ? pegs[0].Width : pegs[0].WidthRequest;
        if (d <= 0) return;

        double content = Math.Max(d, h - below);
        int rows = (n + 1) / 2;
        double rise = rows > 1 ? Math.Clamp((content - d * RoomyRun - d) / (rows - 1), d * MinRise, d * MaxRise) : 0;
        // Short of room, the string under the lowest pegs gives way before the tree does.
        double run = Math.Max(d * TightRun, Math.Min(d * RoomyRun, content - d - (rows - 1) * rise));
        double bottom = Math.Max(d / 2 + (rows - 1) * rise, content - run - d / 2);
        double pitch = n > 1 ? (w - d) / (n - 1) : 0;
        double node = Math.Min(h, content + below * NodeDepth);

        var lines = new Line[n];
        for (int i = 0; i < n; i++)
        {
            int row = Math.Min(i, n - 1 - i);                    // outer strings lowest, the middle ones highest
            double x = n > 1 ? d / 2 + i * pitch : w / 2;
            double y = bottom - row * rise;
            Move(pegs[i], x - d / 2, y - d / 2);
            double share = n > 1 ? i / (double)(n - 1) : 0;
            lines[i] = new Line((float)x, (float)y, (float)node, (float)(d * (ThickGauge + (ThinGauge - ThickGauge) * share)));
        }
        _lines = lines;
        _bow = d * BowRatio;
        if (_vibrations.Length != n)
            _vibrations = Enumerable.Range(0, n).Select(_ => new Vibration()).ToArray();
        for (int i = 0; i < n; i++)
            _vibrations[i].Rate = LowRate + (HighRate - LowRate) * (n > 1 ? i / (double)(n - 1) : 0);
        Invalidate();
    }

    private static void Move(View view, double x, double y)
    {
        if (Math.Abs(view.TranslationX - x) > 0.25) view.TranslationX = x;
        if (Math.Abs(view.TranslationY - y) > 0.25) view.TranslationY = y;
    }

    /// <summary>What each peg is — being tuned, tuned, both or neither — and so
    /// the colour of its string.</summary>
    public void SetStates(IReadOnlyList<(bool Active, bool Tuned)> states)
    {
        if (states.SequenceEqual(_states)) return;
        _states = states.ToArray();
        Invalidate();
    }

    /// <summary>The string that sounds, how hard (0–1) and how far out of tune
    /// in cents (0 once it is in); −1 when none does. Given on every reading of
    /// the tuner, never per frame.</summary>
    public void Sound(int index, double level, double cents)
    {
        if (Motion.Reduced) return;
        for (int i = 0; i < _vibrations.Length; i++)
        {
            var vibration = _vibrations[i];
            bool sounding = i == index;
            vibration.Target = sounding ? MinSwing + (1 - MinSwing) * Math.Clamp(level, 0, 1) : 0;
            if (sounding) vibration.Beat = Math.Min(MaxBeat, Math.Abs(cents) / 50 * MaxBeat);
        }
        if (index >= 0 && index < _vibrations.Length) EnsureTicking();
    }

    /// <summary>One twang, dying away: a string just tuned, or every string in turn.</summary>
    public void Pluck(int index, double strength = 1)
    {
        if (Motion.Reduced || index < 0 || index >= _vibrations.Length) return;
        var vibration = _vibrations[index];
        vibration.Kick = Math.Max(vibration.Kick, Math.Clamp(strength, 0, 1));
        if (vibration.Target <= 0) vibration.Beat = 0;           // a tuned string rings steady
        EnsureTicking();
    }

    private void EnsureTicking()
    {
        if (_ticking || Handler == null) return;                 // not on screen: the next reading tries again
        _ticking = true;
        _clock.Restart();
        new Animation(_ => Advance()).Commit(this, Ticker, length: 3_600_000, repeat: () => _ticking);
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        this.AbortAnimation(Ticker);
    }

    /// <summary>Moves every string a frame on, redraws while any of them moves,
    /// and stops once the last one is still again (drawn straight one last time).</summary>
    private void Advance()
    {
        double dt = Math.Min(0.05, _clock.Elapsed.TotalSeconds); // a stalled frame must not fling a string
        _clock.Restart();
        bool any = false, changed = false;
        foreach (var vibration in _vibrations)
        {
            vibration.Kick *= Math.Exp(-dt / KickFade);
            double goal = Math.Max(vibration.Target, vibration.Kick);
            double tau = goal > vibration.Amplitude ? Attack : Release;
            vibration.Amplitude += (goal - vibration.Amplitude) * (1 - Math.Exp(-dt / tau));
            if (vibration.Amplitude <= Still && goal <= Still)
            {
                vibration.Amplitude = vibration.Kick = 0;
                if (vibration.Offset != 0) { vibration.Offset = 0; changed = true; }
                continue;
            }
            any = true;
            vibration.Phase = (vibration.Phase + Turn * vibration.Rate * dt) % Turn;
            vibration.BeatPhase = (vibration.BeatPhase + Math.PI * vibration.Beat * dt) % Math.PI;
            // Out of tune a string beats — it swells and fades; in tune it rings steady.
            double wobble = vibration.Beat > 0 ? 0.6 + 0.4 * Math.Abs(Math.Cos(vibration.BeatPhase)) : 1;
            vibration.Offset = vibration.Amplitude * wobble * Math.Sin(vibration.Phase) * _bow;
            changed = true;
        }
        if (changed) Invalidate();
        if (!any) StopTicking();
    }

    private Color ColourOf(int index)
    {
        if (index >= _states.Length) return IdleColor;
        var (active, tuned) = _states[index];
        return tuned ? TunedColor : active ? ActiveColor : IdleColor;
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Handler == null) return;                                 // torn down: nothing to draw for
        try
        {
            var lines = _lines;
            var vibrations = _vibrations;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                canvas.StrokeColor = ColourOf(i);
                canvas.StrokeSize = line.Gauge;
                double offset = i < vibrations.Length ? vibrations[i].Offset : 0;
                if (Math.Abs(offset) < 0.05)
                {
                    canvas.DrawLine(line.X, line.Peg, line.X, rect.Height);
                    continue;
                }
                // The first mode of a string pinned at its peg and behind the bar:
                // the same stroke from end to end, so its thickness never changes.
                float span = line.Node - line.Peg, fromX = line.X, fromY = line.Peg;
                for (int k = 1; k <= Steps; k++)
                {
                    float t = k / (float)Steps;
                    float x = line.X + (float)(offset * Math.Sin(Math.PI * t));
                    float y = line.Peg + span * t;
                    canvas.DrawLine(fromX, fromY, x, y);
                    fromX = x;
                    fromY = y;
                }
                if (line.Node < rect.Height) canvas.DrawLine(line.X, line.Node, line.X, rect.Height);
            }
        }
        catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
    }
}

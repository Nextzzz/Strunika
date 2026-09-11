using System.Diagnostics;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Controls;

/// <summary>
/// The tuner's pegs laid out as a guitar's headstock: half the strings on the
/// left and half on the right, each column leaning in towards the top the way
/// the tuners of a 3+3 headstock sit, and a string running from every peg down
/// to where the neck would be — under the tab bar, where it fades out (user
/// design 2026-09-11). The outer strings' pegs sit nearest the nut, as on the
/// instrument, so the strings fan out without crossing.
/// <para>
/// A string wears its peg's colour. The one being tuned shakes while it
/// sounds: wider the harder it is played, dying away with the note, wobbling
/// while it is out of tune and ringing steady once it is in. None of that is
/// drawn per frame (strunika-ui §7): the strings at rest are one canvas,
/// redrawn when a colour changes, and a sounding string is a bowed copy of
/// itself, drawn once and swung by its transform.
/// </para>
/// </summary>
public sealed class Headstock : Grid
{
    // Proportions of the peg, which is measured: the pegs are {t:Size} tokens.
    /// <summary>Clearance between two neighbouring pegs of a column.</summary>
    private const double Gap = 0.08;
    /// <summary>How far each peg of a column sits above the one below it: as
    /// steep as the room allows, never so flat the columns turn into rows.</summary>
    private const double MinRise = 0.45, MaxRise = 0.92;
    /// <summary>The least a column leans in, so it reads as diagonal even when
    /// the rise alone would clear the pegs.</summary>
    private const double MinLean = 0.3;
    /// <summary>String left in view between the lowest pegs and the tab bar's
    /// room: generous when there is space, tight when there is not.</summary>
    private const double RoomyRun = 0.6, TightRun = 0.35;
    /// <summary>Distance between the strings where they meet, under the bar.</summary>
    private const double NutSpacing = 0.2;
    /// <summary>String thickness, lowest string to highest, as on a guitar.</summary>
    private const double ThickGauge = 0.065, ThinGauge = 0.032;
    /// <summary>How far the middle of a sounding string swings.</summary>
    private const double BowRatio = 0.14;

    // Motion.
    /// <summary>Visible swings per second, lowest string to highest. A real
    /// string's hundreds of hertz are a blur; this is what reads as shaking.</summary>
    private const double LowRate = 8, HighRate = 13;
    /// <summary>Seconds: how fast a swing grows, settles and a twang dies away.</summary>
    private const double Attack = 0.04, Release = 0.3, KickFade = 0.45;
    /// <summary>Wobbles per second at 50 cents out of tune.</summary>
    private const double MaxBeat = 4;
    private const double Still = 0.015;
    /// <summary>The resting line under a string that is swinging.</summary>
    private const float RestWhileMoving = 0.35f;
    private const string Ticker = "headstock-swing";
    private const double Turn = 2 * Math.PI;

    public static readonly BindableProperty IdleColorProperty = Colour(nameof(IdleColor), Colors.Gray);
    public static readonly BindableProperty ActiveColorProperty = Colour(nameof(ActiveColor), Colors.Goldenrod);
    public static readonly BindableProperty TunedColorProperty = Colour(nameof(TunedColor), Colors.Gold);

    private static BindableProperty Colour(string name, Color fallback) =>
        BindableProperty.Create(name, typeof(Color), typeof(Headstock), fallback,
            propertyChanged: (b, _, _) => ((Headstock)b).Repaint());

    /// <summary>A string whose peg is neither being tuned nor done.</summary>
    public Color IdleColor { get => (Color)GetValue(IdleColorProperty); set => SetValue(IdleColorProperty, value); }
    /// <summary>The string being tuned: its peg's ring.</summary>
    public Color ActiveColor { get => (Color)GetValue(ActiveColorProperty); set => SetValue(ActiveColorProperty, value); }
    /// <summary>A string marked tuned: its peg's fill.</summary>
    public Color TunedColor { get => (Color)GetValue(TunedColorProperty); set => SetValue(TunedColorProperty, value); }

    /// <summary>One string: the centre of its peg, where it goes under the tab
    /// bar's room (the swinging part ends there) and where it is headed.</summary>
    private readonly record struct Line(Point Peg, Point Edge, Point Nut, float Gauge);

    /// <summary>A string's moving copy, and how it is moving.</summary>
    private sealed class Swing
    {
        public required Grid Holder;             // laid along the string once
        public required GraphicsView Bow;        // the bowed string, swung by its transform
        public double Amplitude, Target, Kick, Phase, Rate, Beat, BeatPhase;
        public bool Moving;
    }

    private readonly GraphicsView _rest;
    private readonly List<Swing> _swings = new();
    private Line[] _lines = Array.Empty<Line>();
    private (bool Active, bool Tuned)[] _states = Array.Empty<(bool, bool)>();
    private float _bow;
    private bool _ticking, _unloaded;
    private readonly Stopwatch _clock = new();

    public Headstock()
    {
        InputTransparent = true;
        _rest = new GraphicsView { Drawable = new RestDrawable(this), BackgroundColor = Colors.Transparent, InputTransparent = true };
        Children.Add(_rest);
        Loaded += (_, _) => _unloaded = false;
        Unloaded += (_, _) => { _unloaded = true; StopTicking(); };
    }

    /// <summary>The height the pegs of <paramref name="count"/> strings need
    /// above the tab bar's room: with the columns as flat as they may lean
    /// (tight), or as steep as they would like (roomy).</summary>
    public static double HeightFor(double peg, int count, bool roomy)
    {
        int rows = (count + 1) / 2;
        return peg + Math.Max(0, rows - 1) * peg * (roomy ? MaxRise : MinRise) + peg * (roomy ? RoomyRun : TightRun);
    }

    /// <summary>
    /// Puts the pegs in their two columns over this control and runs a string
    /// to each. <paramref name="below"/> is how far the control reaches under
    /// the content — the room kept for the tab bar: the strings go on into it
    /// and meet there, out of sight.
    /// </summary>
    public void Arrange(IReadOnlyList<View> pegs, double below)
    {
        double w = Width, h = Height;
        int n = pegs.Count;
        if (w <= 0 || h <= 0 || n == 0) return;
        double d = pegs[0].Width > 0 ? pegs[0].Width : pegs[0].WidthRequest;
        if (d <= 0) return;

        double content = Math.Max(d, h - below);
        int left = (n + 1) / 2, rows = left;
        double rise = rows > 1 ? Math.Clamp((content - d * RoomyRun - d) / (rows - 1), d * MinRise, d * MaxRise) : 0;
        // Short of room, the string under the lowest pegs gives way before the columns do.
        double run = Math.Max(d * TightRun, Math.Min(d * RoomyRun, content - d - (rows - 1) * rise));
        double bottom = Math.Max(d / 2 + (rows - 1) * rise, content - run - d / 2);
        double pitch = d * (1 + Gap);
        double lean = Math.Max(d * MinLean, Math.Sqrt(Math.Max(0, pitch * pitch - rise * rise)));
        if (rows > 1) lean = Math.Min(lean, Math.Max(0, (w / 2 - d) / (rows - 1)));   // never across the middle

        double centre = w / 2, spacing = d * NutSpacing;
        var lines = new Line[n];
        for (int i = 0; i < n; i++)
        {
            bool onLeft = i < left;
            int row = onLeft ? i : n - 1 - i;                    // the outer strings' pegs sit nearest the nut
            var peg = new Point(onLeft ? d / 2 + row * lean : w - d / 2 - row * lean, bottom - row * rise);
            Move(pegs[i], peg.X - d / 2, peg.Y - d / 2);
            var nut = new Point(centre + (i - (n - 1) / 2.0) * spacing, h);
            double t = nut.Y - peg.Y > 1 ? Math.Clamp((content - peg.Y) / (nut.Y - peg.Y), 0, 1) : 1;
            var edge = new Point(peg.X + (nut.X - peg.X) * t, peg.Y + (nut.Y - peg.Y) * t);
            double share = n > 1 ? i / (double)(n - 1) : 0;
            lines[i] = new Line(peg, edge, nut, (float)(d * (ThickGauge + (ThinGauge - ThickGauge) * share)));
        }
        _lines = lines;
        _bow = (float)(d * BowRatio);
        Fit(n);
        for (int i = 0; i < n; i++) Place(i, n);
        _rest.Invalidate();
    }

    private static void Move(View view, double x, double y)
    {
        if (Math.Abs(view.TranslationX - x) > 0.25) view.TranslationX = x;
        if (Math.Abs(view.TranslationY - y) > 0.25) view.TranslationY = y;
    }

    /// <summary>One moving copy per string.</summary>
    private void Fit(int count)
    {
        while (_swings.Count < count)
        {
            var bow = new GraphicsView { Drawable = new BowDrawable(this, _swings.Count), BackgroundColor = Colors.Transparent, InputTransparent = true };
            var holder = new Grid
            {
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true,
                IsVisible = false,
                Children = { bow },
            };
            _swings.Add(new Swing { Holder = holder, Bow = bow });
            Children.Add(holder);
        }
        while (_swings.Count > count)
        {
            Children.Remove(_swings[^1].Holder);
            _swings.RemoveAt(_swings.Count - 1);
        }
    }

    /// <summary>The copy lies along its string from the peg to the edge of the
    /// content, turned about its own middle.</summary>
    private void Place(int index, int count)
    {
        var line = _lines[index];
        var swing = _swings[index];
        double dx = line.Edge.X - line.Peg.X, dy = line.Edge.Y - line.Peg.Y;
        double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double width = 2 * (_bow + 2 * line.Gauge);
        swing.Holder.WidthRequest = width;
        swing.Holder.HeightRequest = length;
        swing.Holder.TranslationX = (line.Peg.X + line.Edge.X) / 2 - width / 2;
        swing.Holder.TranslationY = (line.Peg.Y + line.Edge.Y) / 2 - length / 2;
        // Clockwise degrees that carry the copy's downward axis onto the string.
        swing.Holder.Rotation = Math.Atan2(-dx, dy) * 180 / Math.PI;
        swing.Rate = LowRate + (HighRate - LowRate) * (count > 1 ? index / (double)(count - 1) : 0);
        swing.Bow.Invalidate();
    }

    /// <summary>What each peg is — being tuned, tuned, both or neither — and so
    /// the colour of its string.</summary>
    public void SetStates(IReadOnlyList<(bool Active, bool Tuned)> states)
    {
        if (states.SequenceEqual(_states)) return;
        _states = states.ToArray();
        Repaint();
    }

    /// <summary>The string that sounds, how hard (0–1) and how far out of tune
    /// in cents (0 once it is in); −1 when none does. Given on every reading of
    /// the tuner, never per frame.</summary>
    public void Sound(int index, double level, double cents)
    {
        if (Motion.Reduced) return;
        for (int i = 0; i < _swings.Count; i++)
        {
            var swing = _swings[i];
            swing.Target = i == index ? Math.Clamp(level, 0, 1) : 0;
            if (i == index) swing.Beat = Math.Min(MaxBeat, Math.Abs(cents) / 50 * MaxBeat);
        }
        if (index >= 0 && index < _swings.Count && level > 0) EnsureTicking();
    }

    /// <summary>One twang, dying away: a string just tuned, or every string in turn.</summary>
    public void Pluck(int index, double strength = 1)
    {
        if (Motion.Reduced || index < 0 || index >= _swings.Count) return;
        var swing = _swings[index];
        swing.Kick = Math.Max(swing.Kick, Math.Clamp(strength, 0, 1));
        if (swing.Target <= 0) swing.Beat = 0;                   // a tuned string rings steady
        EnsureTicking();
    }

    private void EnsureTicking()
    {
        if (_ticking || _unloaded) return;
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

    /// <summary>A transform per swinging string and nothing else; the resting
    /// canvas is repainted only when a string starts or stops swinging.</summary>
    private void Advance()
    {
        if (_unloaded) { StopTicking(); return; }
        double dt = Math.Min(0.05, _clock.Elapsed.TotalSeconds);  // a stalled frame must not fling a string
        _clock.Restart();
        bool any = false, restChanged = false;
        foreach (var swing in _swings)
        {
            swing.Kick *= Math.Exp(-dt / KickFade);
            double goal = Math.Max(swing.Target, swing.Kick);
            double tau = goal > swing.Amplitude ? Attack : Release;
            swing.Amplitude += (goal - swing.Amplitude) * (1 - Math.Exp(-dt / tau));
            bool moving = swing.Amplitude > Still;
            if (moving != swing.Moving)
            {
                swing.Moving = moving;
                swing.Holder.IsVisible = moving;
                restChanged = true;
                if (moving) swing.Bow.Invalidate();
            }
            if (!moving) continue;
            any = true;
            swing.Phase = (swing.Phase + Turn * swing.Rate * dt) % Turn;
            swing.BeatPhase = (swing.BeatPhase + Math.PI * swing.Beat * dt) % Math.PI;
            // Out of tune a string beats — it swells and fades; in tune it rings steady.
            double wobble = swing.Beat > 0 ? 0.55 + 0.45 * Math.Abs(Math.Cos(swing.BeatPhase)) : 1;
            double scale = swing.Amplitude * wobble * Math.Sin(swing.Phase);
            NativeTransform.ScaleXAround(swing.Bow, Math.Abs(scale) < 0.001 ? 0.001 : scale);
        }
        if (restChanged) _rest.Invalidate();
        if (!any) StopTicking();
    }

    private Color ColourOf(int index)
    {
        if (index >= _states.Length) return IdleColor;
        var (active, tuned) = _states[index];
        return tuned ? TunedColor : active ? ActiveColor : IdleColor;
    }

    private void Repaint()
    {
        _rest.Invalidate();
        foreach (var swing in _swings)
            if (swing.Moving) swing.Bow.Invalidate();
    }

    /// <summary>Every string at rest. The part of a swinging string that moves
    /// is left faint underneath it; the part under the bar stays as it is.</summary>
    private sealed class RestDrawable(Headstock owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            if (owner.Handler == null) return;
            try
            {
                var lines = owner._lines;
                canvas.StrokeLineCap = LineCap.Round;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var colour = owner.ColourOf(i);
                    bool moving = i < owner._swings.Count && owner._swings[i].Moving;
                    canvas.StrokeSize = line.Gauge;
                    canvas.StrokeColor = moving ? colour.WithAlpha(colour.Alpha * RestWhileMoving) : colour;
                    canvas.DrawLine((float)line.Peg.X, (float)line.Peg.Y, (float)line.Edge.X, (float)line.Edge.Y);
                    canvas.StrokeColor = colour;
                    canvas.DrawLine((float)line.Edge.X, (float)line.Edge.Y, (float)line.Nut.X, (float)line.Nut.Y);
                }
            }
            catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
        }
    }

    /// <summary>A string bowed as far as it ever swings, pinned at both ends —
    /// a real string's first mode. Its transform swings it through the middle.</summary>
    private sealed class BowDrawable(Headstock owner, int index) : IDrawable
    {
        private const int Steps = 24;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (owner.Handler == null || index >= owner._lines.Length || rect.Height <= 0) return;
            try
            {
                var line = owner._lines[index];
                var colour = owner.ColourOf(index);
                float centre = rect.Width / 2, bow = owner._bow, height = rect.Height;
                var path = new PathF();
                path.MoveTo(centre, 0);
                for (int k = 1; k <= Steps; k++)
                {
                    float t = k / (float)Steps;
                    path.LineTo(centre + bow * MathF.Sin(MathF.PI * t), t * height);
                }
                canvas.StrokeLineCap = LineCap.Round;
                canvas.StrokeLineJoin = LineJoin.Round;
                // A soft halo under the string: what a string in motion leaves on the eye.
                canvas.StrokeColor = colour.WithAlpha(colour.Alpha * 0.25f);
                canvas.StrokeSize = line.Gauge * 3;
                canvas.DrawPath(path);
                canvas.StrokeColor = colour;
                canvas.StrokeSize = line.Gauge;
                canvas.DrawPath(path);
            }
            catch (Exception ex) when (NativeTransform.IsTearDown(ex)) { }
        }
    }
}

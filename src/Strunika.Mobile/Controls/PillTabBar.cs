using Strunika.Mobile.Services;

namespace Strunika.Mobile.Controls;

public sealed class PillTab
{
    public PillTab(string icon, string label) { Icon = icon; Label = label; }
    public string Icon { get; }
    public string Label { get; set; }
}

/// <summary>
/// Floating capsule tab bar with an oval selector that follows the finger.
///
/// Behaviour (design decision, strunika-ui §5):
///  - drag the selector left/right; on release it snaps to the nearest tab
///    with a spring; a plain tap jumps to that tab;
///  - touch-down grows the whole capsule to ~1.04, release springs it back
///    to 1.0 with a slight undershoot — a tap therefore reads as a bounce;
///  - light haptic when the selection changes; Reduce Motion removes overshoot.
/// </summary>
public sealed class PillTabBar : GraphicsView, IDrawable
{
    private const float Pad = 6f;
    private const float DragThreshold = 6f;

    // The capsule fills the whole control (spacing comes from Margin): transparent
    // canvas regions show the window backdrop on Windows, so there are none.
    private const float InsetX = 0f, InsetTop = 0f, InsetBottom = 0f;
    private const float LabelSize = 10.5f, LabelMin = 8f;
    /// <summary>Average advance per glyph for the bold UI face, in em. Calibrated
    /// on the dev head: Segoe UI Bold Cyrillic draws about 0.68 em while Win2D
    /// reports 0.59 (13 % short); SF Pro Bold sits near 0.6.</summary>
    private static readonly float LabelEm = DeviceInfo.Platform == DevicePlatform.WinUI ? 0.70f : 0.64f;
    private float _labelFont = LabelSize, _labelFontForWidth = -1;
    private bool _measureLogged;

    public static readonly BindableProperty SelectedIndexProperty =
        BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(PillTabBar), 0, BindingMode.TwoWay,
            propertyChanged: (b, o, n) => ((PillTabBar)b).OnSelectedIndexChanged((int)n));

    public static readonly BindableProperty BarColorProperty = Col(nameof(BarColor), Colors.DimGray);
    public static readonly BindableProperty SeparatorColorProperty = Col(nameof(SeparatorColor), Colors.Gray);
    public static readonly BindableProperty SelectorColorProperty = Col(nameof(SelectorColor), Colors.Goldenrod);
    public static readonly BindableProperty OnSelectorColorProperty = Col(nameof(OnSelectorColor), Colors.Black);
    public static readonly BindableProperty InactiveColorProperty = Col(nameof(InactiveColor), Colors.LightGray);
    public static readonly BindableProperty GlowColorProperty = Col(nameof(GlowColor), Colors.Transparent);

    private static BindableProperty Col(string name, Color fallback) =>
        BindableProperty.Create(name, typeof(Color), typeof(PillTabBar), fallback,
            propertyChanged: (b, o, n) => ((PillTabBar)b).Invalidate());

    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public Color BarColor { get => (Color)GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }
    public Color SeparatorColor { get => (Color)GetValue(SeparatorColorProperty); set => SetValue(SeparatorColorProperty, value); }
    public Color SelectorColor { get => (Color)GetValue(SelectorColorProperty); set => SetValue(SelectorColorProperty, value); }
    public Color OnSelectorColor { get => (Color)GetValue(OnSelectorColorProperty); set => SetValue(OnSelectorColorProperty, value); }
    public Color InactiveColor { get => (Color)GetValue(InactiveColorProperty); set => SetValue(InactiveColorProperty, value); }
    public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }

    public List<PillTab> Tabs { get; } = new();

    public event EventHandler<int>? Selected;

    private double _selectorPos;      // in tab units; fractional while dragging/animating
    private bool _pressed, _dragging;
    private float _downX;
    private double _dragStartPos;

    public PillTabBar()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        HeightRequest = 66;
        StartInteraction += OnStart;
        DragInteraction += OnDrag;
        EndInteraction += OnEnd;
        CancelInteraction += OnCancel;
    }

    private float CapsuleWidth => (float)Width - 2 * InsetX;
    private float ItemWidth => Tabs.Count == 0 ? 0 : (CapsuleWidth - 2 * Pad) / Tabs.Count;

    private bool InsideCapsule(PointF p) =>
        p.X >= InsetX && p.X <= Width - InsetX && p.Y >= InsetTop && p.Y <= Height - InsetBottom;

    // ---- interaction -------------------------------------------------

    private void OnStart(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || Tabs.Count == 0 || !InsideCapsule(e.Touches[0])) return;
        _pressed = true;
        _dragging = false;
        _downX = e.Touches[0].X;
        _dragStartPos = _selectorPos;
        this.AbortAnimation("snap");
        this.AbortAnimation("scale");
        this.ScaleTo(Motion.Reduced ? 1.0 : 1.04, 120, Easing.CubicOut);
    }

    private void OnDrag(object? sender, TouchEventArgs e)
    {
        if (!_pressed || e.Touches.Length == 0) return;
        float dx = e.Touches[0].X - _downX;
        if (!_dragging && Math.Abs(dx) > DragThreshold)
            _dragging = true;
        if (!_dragging) return;
        _selectorPos = Math.Clamp(_dragStartPos + dx / ItemWidth, 0, Tabs.Count - 1);
        Invalidate();
    }

    private void OnEnd(object? sender, TouchEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        int target = _dragging
            ? (int)Math.Round(_selectorPos)
            : HitIndex(e.Touches.Length > 0 ? e.Touches[0].X : _downX);
        _dragging = false;
        SnapTo(target);
        ReleaseScale();
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        _dragging = false;
        SnapTo(SelectedIndex);
        ReleaseScale();
    }

    private int HitIndex(float x) =>
        Math.Clamp((int)((x - InsetX - Pad) / ItemWidth), 0, Tabs.Count - 1);

    private void ReleaseScale()
    {
        this.AbortAnimation("scale");
        // SpringOut from 1.04 → 1.0 dips slightly below 1 before settling: the "bounce".
        this.ScaleTo(1.0, (uint)(Motion.Reduced ? 120 : 350), Motion.Spring);
    }

    private void SnapTo(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, Tabs.Count - 1));
        AnimateSelector(index);
        if (index != SelectedIndex)
        {
            SelectedIndex = index;   // raises Selected via OnSelectedIndexChanged
            Haptics.Default.Selection();
        }
    }

    private void AnimateSelector(int index)
    {
        this.AbortAnimation("snap");
        double from = _selectorPos, to = index;
        if (Math.Abs(from - to) < 0.001) { _selectorPos = to; Invalidate(); return; }
        new Animation(v => { _selectorPos = v; Invalidate(); }, from, to)
            .Commit(this, "snap", 16, (uint)(Motion.Reduced ? 150 : 300), Motion.Spring);
    }

    private void OnSelectedIndexChanged(int index)
    {
        if (!_pressed)
            AnimateSelector(index);   // programmatic change: glide there
        Selected?.Invoke(this, index);
    }

    /// <summary>Re-read labels (after a language change) and redraw.</summary>
    public void Refresh() { _labelFontForWidth = -1; Invalidate(); }

    /// <summary>One font size for all labels, shrunk (down to <see cref="LabelMin"/>)
    /// until the widest label fits its slot — "Налаштування" on a compact phone.</summary>
    private float LabelFont(ICanvas canvas, float itemW)
    {
        if (Math.Abs(_labelFontForWidth - itemW) < 0.5f) return _labelFont;
        _labelFontForWidth = itemW;
        float size = LabelSize, room = itemW - 4f;
        foreach (var tab in Tabs)
        {
            // Win2D under-reports bold Cyrillic, so the measurement is checked
            // against a per-glyph estimate (about 0.62 em for a bold sans) and
            // the wider of the two decides.
            float measured = canvas.GetStringSize(tab.Label, Microsoft.Maui.Graphics.Font.DefaultBold, LabelSize).Width * 1.08f;
            float estimated = tab.Label.Length * LabelEm * LabelSize;
            float w = Math.Max(measured, estimated);
            if (!_measureLogged) Strunika.Core.Diagnostics.FileLog.Info($"tabbar label \"{tab.Label}\": measured {measured:0.0}, estimated {estimated:0.0}, slot {itemW:0.0}");
            if (w > room) size = Math.Min(size, LabelSize * room / w);
        }
        _measureLogged = true;
        _labelFont = Math.Max(LabelMin, size);
        return _labelFont;
    }

    // ---- drawing -----------------------------------------------------

    public void Draw(ICanvas canvas, RectF rect)
    {
        // The platform can still call Draw while the window is being torn
        // down, on a canvas whose session is already gone; every call then
        // throws inside Maui.Graphics. There is nothing left to draw for.
        if (Handler == null) return;                                 // torn down: nothing to draw for
        try { DrawCore(canvas, rect); }
        catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
    }

    private void DrawCore(ICanvas canvas, RectF rect)
    {
        float w = rect.Width - 2 * InsetX, h = rect.Height - InsetTop - InsetBottom;
        if (w <= 0 || h <= 0) return;
        canvas.Translate(InsetX, InsetTop);

        // capsule
        canvas.FillColor = BarColor;
        canvas.FillRoundedRectangle(0, 0, w, h, h / 2);
        canvas.StrokeColor = SeparatorColor;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(0.5f, 0.5f, w - 1, h - 1, (h - 1) / 2);

        int n = Tabs.Count;
        if (n == 0) return;
        float itemW = (w - 2 * Pad) / n;
        float selH = h - 2 * Pad;
        float selX = Pad + (float)_selectorPos * itemW;

        // selector glow + fill
        // Glow stays inside the capsule: max spread < Pad, so it never pokes
        // out at the first/last tab.
        for (int i = 4; i >= 1; i--)
        {
            float spread = i * 1.2f;
            canvas.FillColor = GlowColor.WithAlpha(GlowColor.Alpha * 0.12f);
            canvas.FillRoundedRectangle(selX - spread, Pad - spread + 1, itemW + 2 * spread, selH + 2 * spread, (selH + 2 * spread) / 2);
        }
        canvas.FillColor = SelectorColor;
        canvas.FillRoundedRectangle(selX, Pad, itemW, selH, selH / 2);

        // items: the icon and its word are one block, centred together in the
        // capsule — centring the icon alone left the word hanging below it.
        const float iconSize = 24f, iconGap = 3f;
        float labelSize = LabelFont(canvas, itemW);
        float labelH = labelSize * 1.25f;
        float blockTop = (h - (iconSize + iconGap + labelH)) / 2;
        for (int i = 0; i < n; i++)
        {
            float cx = Pad + itemW * (i + 0.5f);
            float t = 1f - (float)Math.Min(1.0, Math.Abs(_selectorPos - i));   // 1 = fully selected
            var color = Lerp(InactiveColor, OnSelectorColor, t);

            var glyph = Icons.Get(Tabs[i].Icon);
            if (glyph != null)
            {
                canvas.SaveState();
                canvas.Translate(cx - iconSize / 2, blockTop);
                Icons.Draw(canvas, glyph, color, strokeSize: 2.2f);
                canvas.RestoreState();
            }

            canvas.FontColor = color;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = labelSize;
            canvas.DrawString(Tabs[i].Label, cx - itemW / 2, blockTop + iconSize + iconGap, itemW, labelH,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private static Color Lerp(Color a, Color b, float t) =>
        new(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t,
            a.Blue + (b.Blue - a.Blue) * t, a.Alpha + (b.Alpha - a.Alpha) * t);
}

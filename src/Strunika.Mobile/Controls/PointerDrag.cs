namespace Strunika.Mobile.Controls;

/// <summary>
/// Press–drag–release on a view, reliable past the view's edge. MAUI's
/// PanGestureRecognizer on Windows ends a drag released outside the element
/// as "cancelled" and reports the start offset on the way out, snapping a
/// slider back to where the drag began; here Windows captures the pointer
/// natively, so the offset keeps coming wherever the mouse goes. Other
/// platforms use the pan gesture, whose native recogniser tracks outside the
/// view on its own. A press without movement is a tap.
/// </summary>
public static class PointerDrag
{
    public sealed class Callbacks
    {
        /// <summary>Pointer down at x (view coordinates).</summary>
        public Action<double>? Started { get; init; }
        /// <summary>Horizontal offset from the press, in view coordinates.</summary>
        public Action<double>? Moved { get; init; }
        /// <summary>Pointer up or capture lost after a real drag.</summary>
        public Action? Ended { get; init; }
        /// <summary>Pointer up without a real drag, at the press point.</summary>
        public Action<Point>? Tapped { get; init; }
    }

    private const double TapSlop = 3;

    public static void Attach(View surface, Callbacks c)
    {
#if WINDOWS
        surface.HandlerChanged += (_, _) => AttachWindows(surface, c);
        if (surface.Handler != null) AttachWindows(surface, c);
#else
        AttachGestures(surface, c);
#endif
    }

#if WINDOWS
    private static void AttachWindows(View surface, Callbacks c)
    {
        if (surface.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement el || el.Tag is "pointer-drag") return;
        el.Tag = "pointer-drag";                                           // once per platform element
        bool captured = false, moved = false;
        double startX = 0, startY = 0;
        el.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(el).Position;
            if (!el.CapturePointer(e.Pointer)) return;
            captured = true; moved = false; startX = p.X; startY = p.Y;
            c.Started?.Invoke(p.X);
            e.Handled = true;
        };
        el.PointerMoved += (_, e) =>
        {
            if (!captured) return;
            var p = e.GetCurrentPoint(el).Position;
            double dx = p.X - startX;
            if (Math.Abs(dx) > TapSlop || Math.Abs(p.Y - startY) > TapSlop) moved = true;
            if (moved) c.Moved?.Invoke(dx);
            e.Handled = true;
        };
        el.PointerReleased += (_, e) =>
        {
            if (!captured) return;
            captured = false;
            el.ReleasePointerCapture(e.Pointer);
            if (moved) c.Ended?.Invoke(); else c.Tapped?.Invoke(new Point(startX, startY));
            e.Handled = true;
        };
        el.PointerCaptureLost += (_, _) =>
        {
            if (!captured) return;
            captured = false;
            if (moved) c.Ended?.Invoke(); else c.Tapped?.Invoke(new Point(startX, startY));
        };
    }
#else
    private static void AttachGestures(View surface, Callbacks c)
    {
        bool panning = false;
        double lastTotal = 0;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panning = true; lastTotal = 0;
                    c.Started?.Invoke(0);
                    break;
                case GestureStatus.Running:
                    if (!panning) return;
                    if (Math.Abs(e.TotalX) < 0.5 && Math.Abs(lastTotal) > 12) return;   // stray reset before a cancel
                    lastTotal = e.TotalX;
                    c.Moved?.Invoke(e.TotalX);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (!panning) return;
                    panning = false;
                    c.Ended?.Invoke();
                    break;
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) => { var p = e.GetPosition(surface); if (p != null) c.Tapped?.Invoke(p.Value); };
        surface.GestureRecognizers.Add(pan);
        surface.GestureRecognizers.Add(tap);
    }
#endif
}

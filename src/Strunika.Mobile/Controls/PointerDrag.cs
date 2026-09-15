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
        /// <summary>The pointer came down here, before anything else is known
        /// — a drag, a tap or a hold. On iOS it comes at once, from the touch.</summary>
        public Action<Point>? Pressed { get; init; }
        /// <summary>Pointer down at x (view coordinates). On iOS it comes when the
        /// drag begins, with the point the finger first touched.</summary>
        public Action<double>? Started { get; init; }
        /// <summary>Horizontal offset from the press, in view coordinates.</summary>
        public Action<double>? Moved { get; init; }
        /// <summary>Both offsets, for something dragged about a grid rather than
        /// along a line. Raised with <see cref="Moved"/>, never instead of it.</summary>
        public Action<double, double>? Dragged { get; init; }
        /// <summary>Pointer up or capture lost after a real drag.</summary>
        public Action? Ended { get; init; }
        /// <summary>Pointer up without a real drag, at the press point.</summary>
        public Action<Point>? Tapped { get; init; }
        /// <summary>The pointer has stayed down and still for a moment
        /// (<see cref="HoldDrag.Delay"/>), at the press point. It comes before any
        /// drag, and not at all once the pointer has moved or lifted.</summary>
        public Action<Point>? Held { get; init; }
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
        int press = 0;
        double startX = 0, startY = 0;
        el.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(el).Position;
            if (!el.CapturePointer(e.Pointer)) return;
            captured = true; moved = false; startX = p.X; startY = p.Y;
            c.Pressed?.Invoke(new Point(p.X, p.Y));
            c.Started?.Invoke(p.X);
            if (c.Held != null)
            {
                int id = ++press;
                surface.Dispatcher.DispatchDelayed(HoldDrag.Delay, () =>
                {
                    if (id == press && captured && !moved) c.Held?.Invoke(new Point(startX, startY));
                });
            }
            e.Handled = true;
        };
        el.PointerMoved += (_, e) =>
        {
            if (!captured) return;
            var p = e.GetCurrentPoint(el).Position;
            double dx = p.X - startX;
            double dy = p.Y - startY;
            if (Math.Abs(dx) > TapSlop || Math.Abs(dy) > TapSlop) moved = true;
            if (moved) { c.Moved?.Invoke(dx); c.Dragged?.Invoke(dx, dy); }
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
        double lastTotal = 0, downX = 0;
#if IOS
        // A pan on iOS begins only once the finger has moved and never says where
        // it came down; a recognizer that only watches the touch go down supplies
        // that point, so Started gets it as it does on Windows (user report 2026-09-14).
        bool pressed = false, strayed = false;
        int press = 0;
        double atX = 0, atY = 0;
        void Watch()
        {
            if (surface.Handler?.PlatformView is not UIKit.UIView view) return;
            if (view.GestureRecognizers?.Any(g => g is Platforms.iOS.TouchDownRecognizer) == true) return;
            if (c.Held == null)
            {
                view.AddGestureRecognizer(new Platforms.iOS.TouchDownRecognizer(point =>
                {
                    downX = point.X;
                    c.Pressed?.Invoke(new Point(point.X, point.Y));
                }));
                return;
            }
            // A hold is wanted too: the recognizer follows the finger to its end,
            // so a still press can be told from a move and from a lift.
            view.AddGestureRecognizer(new Platforms.iOS.TouchDownRecognizer(
                down: point =>
                {
                    downX = atX = point.X;
                    atY = point.Y;
                    c.Pressed?.Invoke(new Point(point.X, point.Y));
                    pressed = true;
                    strayed = false;
                    int id = ++press;
                    surface.Dispatcher.DispatchDelayed(HoldDrag.Delay, () =>
                    {
                        if (id == press && pressed && !strayed) c.Held?.Invoke(new Point(atX, atY));
                    });
                },
                moved: point =>
                {
                    if (Math.Abs(point.X - atX) > HoldDrag.Slop || Math.Abs(point.Y - atY) > HoldDrag.Slop) strayed = true;
                },
                up: () => pressed = false));
        }
        surface.HandlerChanged += (_, _) => Watch();
        Watch();
#endif
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panning = true; lastTotal = 0;
                    c.Started?.Invoke(downX);
                    break;
                case GestureStatus.Running:
                    if (!panning) return;
                    if (Math.Abs(e.TotalX) < 0.5 && Math.Abs(lastTotal) > 12) return;   // stray reset before a cancel
                    lastTotal = e.TotalX;
                    c.Moved?.Invoke(e.TotalX);
                    c.Dragged?.Invoke(e.TotalX, e.TotalY);
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

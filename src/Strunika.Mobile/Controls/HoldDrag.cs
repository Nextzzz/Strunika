namespace Strunika.Mobile.Controls;

/// <summary>
/// Press and hold, then drag — for things inside a scroll view, where a drag
/// that took the finger at once would take the scrolling away. Nothing happens
/// until the finger has been still for a moment; moving before that is left to
/// the scroll view. Once the hold has taken, the finger is followed until it
/// lifts (user request 2026-09-14: any chord in the beat view can be dragged).
/// On iOS it is a long press, which the scroll view's own pan and the hold rule
/// out of each other; on Windows a held pointer, captured once the hold takes.
/// </summary>
public static class HoldDrag
{
    public sealed class Callbacks
    {
        /// <summary>The hold took, at this point (view coordinates).</summary>
        public Action<Point>? Held { get; init; }
        /// <summary>After the hold: where the finger is now.</summary>
        public Action<Point>? Moved { get; init; }
        /// <summary>The finger lifted after the hold, here.</summary>
        public Action<Point>? Released { get; init; }
        /// <summary>The touch was taken away after the hold.</summary>
        public Action? Cancelled { get; init; }
    }

    /// <summary>How long a finger must stay still to be a hold.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(320);
    /// <summary>How far it may wander meanwhile, in points.</summary>
    public const double Slop = 8;

    public static void Attach(View surface, Callbacks c)
    {
#if WINDOWS || IOS
        surface.HandlerChanged += (_, _) => Hook(surface, c);
        Hook(surface, c);
#endif
    }

#if WINDOWS
    private static void Hook(View surface, Callbacks c)
    {
        if (surface.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement el || el.Tag is "hold-drag") return;
        el.Tag = "hold-drag";
        int press = 0;
        bool down = false, holding = false;
        Windows.Foundation.Point start = default;
        Microsoft.UI.Xaml.Input.Pointer? pointer = null;
        el.PointerPressed += (_, e) =>
        {
            start = e.GetCurrentPoint(el).Position;
            pointer = e.Pointer;
            down = true;
            holding = false;
            int id = ++press;
            surface.Dispatcher.DispatchDelayed(Delay, () =>
            {
                if (id != press || !down || holding || pointer == null) return;
                holding = el.CapturePointer(pointer);
                if (holding) c.Held?.Invoke(new Point(start.X, start.Y));
            });
        };
        el.PointerMoved += (_, e) =>
        {
            var p = e.GetCurrentPoint(el).Position;
            if (holding) { c.Moved?.Invoke(new Point(p.X, p.Y)); e.Handled = true; return; }
            if (down && (Math.Abs(p.X - start.X) > Slop || Math.Abs(p.Y - start.Y) > Slop)) down = false;   // moved first: that is scrolling
        };
        el.PointerReleased += (_, e) =>
        {
            down = false;
            if (!holding) return;
            holding = false;
            var p = e.GetCurrentPoint(el).Position;
            el.ReleasePointerCapture(e.Pointer);
            c.Released?.Invoke(new Point(p.X, p.Y));
            e.Handled = true;
        };
        el.PointerCaptureLost += (_, _) =>
        {
            down = false;
            if (!holding) return;
            holding = false;
            c.Cancelled?.Invoke();
        };
    }
#elif IOS
    private static void Hook(View surface, Callbacks c)
    {
        if (surface.Handler?.PlatformView is not UIKit.UIView view) return;
        if (view.GestureRecognizers?.Any(g => g.Name == "hold-drag") == true) return;
        var hold = new UIKit.UILongPressGestureRecognizer(r =>
        {
            var at = r.LocationInView(view);
            var point = new Point(at.X, at.Y);
            switch (r.State)
            {
                case UIKit.UIGestureRecognizerState.Began: c.Held?.Invoke(point); break;
                case UIKit.UIGestureRecognizerState.Changed: c.Moved?.Invoke(point); break;
                case UIKit.UIGestureRecognizerState.Ended: c.Released?.Invoke(point); break;
                case UIKit.UIGestureRecognizerState.Cancelled: c.Cancelled?.Invoke(); break;
            }
        })
        {
            MinimumPressDuration = Delay.TotalSeconds,
            AllowableMovement = (float)Slop,
            Name = "hold-drag",
        };
        view.AddGestureRecognizer(hold);
    }
#endif
}

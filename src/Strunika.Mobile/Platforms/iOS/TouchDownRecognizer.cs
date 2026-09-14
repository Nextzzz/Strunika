using CoreGraphics;
using Foundation;
using UIKit;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// Watches a finger on a view and recognises nothing: it never begins, cancels
/// and delays no touches, and lets every other gesture recognise alongside it.
/// MAUI's pan only begins once the finger has moved and never reports where it
/// first touched; a drag begun in the middle of the song's map was taken as
/// begun at its left edge and threw the window back to the start (user report
/// 2026-09-14). This reports the touch going down — and, when asked, its moves
/// and its end, which is what telling a still hold from a move needs.
/// </summary>
internal sealed class TouchDownRecognizer : UIGestureRecognizer
{
    private readonly Action<CGPoint> _down;
    private readonly Action<CGPoint>? _moved;
    private readonly Action? _up;
    private bool _pressed;

    public TouchDownRecognizer(Action<CGPoint> down, Action<CGPoint>? moved = null, Action? up = null)
    {
        _down = down;
        _moved = moved;
        _up = up;
        CancelsTouchesInView = false;
        DelaysTouchesBegan = false;
        DelaysTouchesEnded = false;
        ShouldRecognizeSimultaneously = (_, _) => true;
    }

    /// <summary>Only the point was wanted: out of the way at once.</summary>
    private bool PointOnly => _moved == null && _up == null;

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        base.TouchesBegan(touches, evt);
        if (!_pressed && touches.AnyObject is UITouch touch && View is { } view)
        {
            _pressed = true;
            _down(touch.LocationInView(view));
        }
        if (PointOnly)
        {
            _pressed = false;
            State = UIGestureRecognizerState.Failed;
        }
    }

    public override void TouchesMoved(NSSet touches, UIEvent evt)
    {
        base.TouchesMoved(touches, evt);
        if (_pressed && touches.AnyObject is UITouch touch && View is { } view)
            _moved?.Invoke(touch.LocationInView(view));
    }

    public override void TouchesEnded(NSSet touches, UIEvent evt)
    {
        base.TouchesEnded(touches, evt);
        Lift();
        State = UIGestureRecognizerState.Failed;
    }

    public override void TouchesCancelled(NSSet touches, UIEvent evt)
    {
        base.TouchesCancelled(touches, evt);
        Lift();
        State = UIGestureRecognizerState.Failed;
    }

    /// <summary>Failed or set aside without an end of its own: the finger is gone all the same.</summary>
    public override void Reset()
    {
        base.Reset();
        Lift();
    }

    private void Lift()
    {
        if (!_pressed) return;
        _pressed = false;
        _up?.Invoke();
    }
}

using CoreGraphics;
using Foundation;
using UIKit;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// Notes where a finger comes down on a view, and recognises nothing: it fails
/// at once, cancels and delays no touches, and lets every other gesture
/// recognise alongside it. MAUI's pan only begins once the finger has moved
/// and never reports where it first touched; a drag begun in the middle of the
/// song's map was taken as begun at its left edge and threw the window back to
/// the start (user report 2026-09-14). This is where that point comes from.
/// </summary>
internal sealed class TouchDownRecognizer : UIGestureRecognizer
{
    private readonly Action<CGPoint> _down;

    public TouchDownRecognizer(Action<CGPoint> down)
    {
        _down = down;
        CancelsTouchesInView = false;
        DelaysTouchesBegan = false;
        DelaysTouchesEnded = false;
        ShouldRecognizeSimultaneously = (_, _) => true;
    }

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        base.TouchesBegan(touches, evt);
        if (touches.AnyObject is UITouch touch && View is { } view)
            _down(touch.LocationInView(view));
        State = UIGestureRecognizerState.Failed;
    }
}

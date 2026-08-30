namespace Strunika.Mobile.Controls;

/// <summary>
/// Per-frame translate/scale without allocations. MAUI's own
/// <c>TranslationX</c> rebuilds a native transform object on every change;
/// on Windows each of those is a COM wrapper, and the XAML runtime answers a
/// stream of them by inducing full garbage collections — the periodic stall
/// on the song page. Here the native transform is created once and only its
/// numbers move. Other platforms take the plain MAUI property.
/// </summary>
public static class NativeTransform
{
    public static void TranslateX(VisualElement view, double x)
    {
        try
        {
#if WINDOWS
            if (Composite(view) is { } ct) { if (ct.TranslateX != x) ct.TranslateX = x; return; }
#endif
            if (view.TranslationX != x) view.TranslationX = x;
        }
        catch (Exception ex) when (IsTearDown(ex)) { }
    }

    /// <summary>A native element already destroyed while the window closes answers
    /// with E_INVALIDARG or a disposed/COM error; there is nothing left to move.</summary>
    public static bool IsTearDown(Exception ex) =>
        ex is ArgumentException or ObjectDisposedException or NullReferenceException or System.Runtime.InteropServices.COMException;

    /// <summary>Scale from the left edge (anchor x = 0).</summary>
    public static void ScaleX(VisualElement view, double s)
    {
        try
        {
#if WINDOWS
            if (Composite(view) is { } ct) { if (ct.ScaleX != s) ct.ScaleX = s; return; }
#endif
            view.AnchorX = 0;
            if (view.ScaleX != s) view.ScaleX = s;
        }
        catch (Exception ex) when (IsTearDown(ex)) { }
    }

#if WINDOWS
    private static bool _logged;

    private static Microsoft.UI.Xaml.Media.CompositeTransform? Composite(VisualElement view)
    {
        if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement el)
        {
            if (!_logged) { _logged = true; Strunika.Core.Diagnostics.FileLog.Info($"native transform: handler {(view.Handler == null ? "null" : view.Handler.GetType().Name)}, platform view {view.Handler?.PlatformView?.GetType().FullName ?? "null"}"); }
            return null;
        }
        if (el.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform ct)
        {
            ct = new Microsoft.UI.Xaml.Media.CompositeTransform();
            el.RenderTransform = ct;
        }
        // MAUI centres its own transform origin; scaling the seek-bar fill must
        // grow from the left edge, and for a translation the origin is moot.
        if (el.RenderTransformOrigin.X != 0 || el.RenderTransformOrigin.Y != 0)
            el.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        return ct;
    }
#endif
}

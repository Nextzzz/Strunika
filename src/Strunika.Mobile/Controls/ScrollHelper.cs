namespace Strunika.Mobile.Controls;

/// <summary>
/// Scrolling a list back to its very top. <c>CollectionView.ScrollTo</c> can
/// only put an <i>item</i> at the top, which on a list with a header (the
/// Songs list reserves 240 pt for its pinned header) leaves the header above
/// the viewport — it reads as a jump downwards. The platform scroller has no
/// such blind spot.
/// </summary>
public static class ScrollHelper
{
    public static void ToTop(CollectionView view)
    {
        try
        {
#if WINDOWS
            if (view.Handler?.PlatformView is Microsoft.UI.Xaml.DependencyObject root && FindScroller(root) is { } scroller)
                scroller.ChangeView(null, 0, null, true);
#elif IOS
            if (view.Handler?.PlatformView is UIKit.UIScrollView scroll)
                scroll.SetContentOffset(new CoreGraphics.CGPoint(0, -scroll.ContentInset.Top), false);
#endif
        }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("scroll to top", ex); }
    }

#if WINDOWS
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScroller(Microsoft.UI.Xaml.DependencyObject root)
    {
        if (root is Microsoft.UI.Xaml.Controls.ScrollViewer found) return found;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            if (FindScroller(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { } scroller)
                return scroller;
        return null;
    }
#endif
}

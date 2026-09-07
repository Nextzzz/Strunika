namespace Strunika.Mobile.Theme;

/// <summary>
/// The window's safe-area insets, for pages that lay their bottom edge out by
/// hand. MAUI 10 pads every layout by the safe area on its own (Container),
/// which is right for content and wrong for what floats over it: the tab
/// bar, the song page's transport and its bottom sheet want the whole screen
/// and a margin of their own choosing above the home indicator.
/// </summary>
public static class SafeArea
{
    /// <summary>Points under the home indicator (34 on a Face ID phone, 0 elsewhere).</summary>
    public static double Bottom
    {
        get
        {
#if IOS
            try
            {
                var window = UIKit.UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIKit.UIWindowScene>().SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)
                    ?? UIKit.UIApplication.SharedApplication.Windows.FirstOrDefault();
                return window?.SafeAreaInsets.Bottom ?? 0;
            }
            catch { return 0; }
#else
            return 0;
#endif
        }
    }

    /// <summary>Where a floating bar's bottom edge goes: 21 pt above the screen's
    /// edge on a phone with a home indicator (the distance iOS 26 keeps its own
    /// tab bar at), 16 pt on one without.</summary>
    public static double FloatingBarMargin => Bottom > 0 ? 21 : 16;
}

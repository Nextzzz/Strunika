namespace Strunika.Mobile.Services;

/// <summary>
/// Honour the system "Reduce Motion" setting: springs lose their overshoot
/// and decorative animations become short fades.
/// </summary>
public static class Motion
{
    public static bool Reduced
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }

    /// <summary>Spring easing, or a plain ease-out when motion is reduced.</summary>
    public static Easing Spring => Reduced ? Easing.CubicOut : Easing.SpringOut;
}

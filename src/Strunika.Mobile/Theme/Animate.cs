namespace Strunika.Mobile.Theme;

/// <summary>
/// The animation extensions under the names MAUI 10 gives them
/// (<c>FadeToAsync</c> …), for the head that is still on MAUI 9 (Windows): the
/// old names are obsolete on 10 and warn on every build, and the new ones
/// do not exist on 9. The shared code uses the new names; here they are
/// mapped to the old ones where that is all there is.
/// </summary>
public static class Animate
{
#if !NET10_0_OR_GREATER
    public static Task<bool> FadeToAsync(this VisualElement view, double opacity, uint length = 250, Easing? easing = null) =>
        Microsoft.Maui.Controls.ViewExtensions.FadeTo(view, opacity, length, easing);

    public static Task<bool> ScaleToAsync(this VisualElement view, double scale, uint length = 250, Easing? easing = null) =>
        Microsoft.Maui.Controls.ViewExtensions.ScaleTo(view, scale, length, easing);

    public static Task<bool> TranslateToAsync(this VisualElement view, double x, double y, uint length = 250, Easing? easing = null) =>
        Microsoft.Maui.Controls.ViewExtensions.TranslateTo(view, x, y, length, easing);

    public static Task DisplayAlertAsync(this Page page, string title, string message, string cancel) =>
        page.DisplayAlert(title, message, cancel);

    public static Task<bool> DisplayAlertAsync(this Page page, string title, string message, string accept, string cancel) =>
        page.DisplayAlert(title, message, accept, cancel);

    public static Task<string> DisplayActionSheetAsync(this Page page, string title, string? cancel, string? destruction, params string[] buttons) =>
        page.DisplayActionSheet(title, cancel, destruction, buttons);
#endif
}

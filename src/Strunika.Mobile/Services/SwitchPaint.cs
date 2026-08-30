using Microsoft.Maui.Platform;

namespace Strunika.Mobile.Services;

/// <summary>
/// The off state of a platform switch, painted with brand colours and kept in
/// step with the theme. Every live switch is repainted from one subscription
/// held here: a subscription per handler outlived its control — repainting a
/// closed page threw "PlatformView cannot be null here" — and kept every
/// switch ever created alive. The list holds weak references, so a switch that
/// is gone simply drops out.
/// </summary>
public static class SwitchPaint
{
#if WINDOWS
    private static readonly List<WeakReference<Microsoft.UI.Xaml.Controls.ToggleSwitch>> Live = new();
#elif IOS
    private static readonly List<WeakReference<UIKit.UISwitch>> Live = new();
#endif

#if WINDOWS || IOS
    private static bool _watching;

    private static void Watch()
    {
        if (_watching) return;
        _watching = true;
        AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.Theme)) PaintAll(); };
        if (Application.Current != null) Application.Current.RequestedThemeChanged += (_, _) => PaintAll();
    }

    private static void PaintAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (Live[i].TryGetTarget(out var view)) Paint(view);
            else Live.RemoveAt(i);                               // the switch is gone
        }
    }
#endif

#if WINDOWS
    public static void Track(Microsoft.UI.Xaml.Controls.ToggleSwitch view)
    {
        Watch();
        Live.Add(new WeakReference<Microsoft.UI.Xaml.Controls.ToggleSwitch>(view));
        if (view.IsLoaded) Paint(view);
        else view.Loaded += (_, _) => Paint(view);               // the resources are only writable once loaded
    }

    private static void Paint(Microsoft.UI.Xaml.Controls.ToggleSwitch view)
    {
        var res = view.Resources;
        // WinUI quirk: the resting keys are Brushes, but the PointerOver /
        // Pressed keys are plain Colors (the storyboards animate Fill with a
        // Color) — a Brush under those keys renders as nothing.
        foreach (var (key, token, isBrush) in new[]
        {
            ("ToggleSwitchFillOff", "Separator", true), ("ToggleSwitchFillOffPointerOver", "Separator", false), ("ToggleSwitchFillOffPressed", "Separator", false),
            ("ToggleSwitchStrokeOff", "Dim", true), ("ToggleSwitchStrokeOffPointerOver", "Dim", false), ("ToggleSwitchStrokeOffPressed", "Dim", false),
        })
        {
            var colour = Theme.Tokens.Current(token).ToWindowsColor();
            try
            {
                if (isBrush && res.TryGetValue(key, out var existing) && existing is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                    brush.Color = colour;
                else if (isBrush)
                    res[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(colour);
                else
                    res[key] = colour;
            }
            catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error($"switch resource {key}", ex); }
        }
    }
#elif IOS
    public static void Track(UIKit.UISwitch view)
    {
        Watch();
        Live.Add(new WeakReference<UIKit.UISwitch>(view));
        Paint(view);
    }

    /// <summary>UISwitch's off track is a faint grey that vanishes on the warm
    /// surfaces: paint it with the Separator token.</summary>
    private static void Paint(UIKit.UISwitch view)
    {
        view.BackgroundColor = Theme.Tokens.Current("Separator").ToPlatform();
        view.Layer.CornerRadius = 15.5f;
        view.ClipsToBounds = true;
    }
#endif
}

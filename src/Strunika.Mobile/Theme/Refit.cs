using Strunika.Mobile.Localization;

namespace Strunika.Mobile.Theme;

/// <summary>
/// Anything laid out from measured text — a row that hides a word when the
/// title needs the room, a wave that takes what the brand name leaves, a
/// quick row that wraps its last button — fits itself when it is resized.
/// Two things change the words and the sizes without resizing anything: a
/// language change and a new size class (the dev window, an iPad split).
/// Every such fit registers here and is run again after either, once the
/// bindings have applied. The rule: a fit that reads text must be watched
/// here, not only by SizeChanged.
/// </summary>
public static class Refit
{
    public static void Watch(BindableObject owner, Action fit)
    {
        Loc.Instance.PropertyChanged += (_, _) => owner.Dispatcher.Dispatch(fit);
        Metrics.Instance.PropertyChanged += (_, e) =>
        {
            // Scale and class are what change sizes; ContentInset ticks on every resize and is handled by layout itself.
            if (e.PropertyName is nameof(Metrics.Scale) or nameof(Metrics.HeroScale) or nameof(Metrics.Class))
                owner.Dispatcher.Dispatch(fit);
        };
    }
}

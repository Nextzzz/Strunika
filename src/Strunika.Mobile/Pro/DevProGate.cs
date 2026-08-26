namespace Strunika.Mobile.Pro;

/// <summary>
/// Development-time entitlement source: a toggle under "Expert settings".
/// Always available on the Windows head (there is no StoreKit there);
/// on iOS it exists only in DEBUG / TestFlight builds. The store-backed
/// gate (M6) will be composed with this one — whichever says Pro wins.
/// </summary>
public sealed class DevProGate : IProGate
{
    private const string Key = "dev_pro_override";

    public static bool IsAvailable =>
#if WINDOWS || DEBUG
        true;
#else
        false;
#endif

    public bool IsPro
    {
        get => IsAvailable && Preferences.Default.Get(Key, DefaultValue);
        set
        {
            if (!IsAvailable || value == IsPro)
                return;
            Preferences.Default.Set(Key, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>On the Windows head everything is unlocked out of the box;
    /// on a debug iPhone build the developer flips it on deliberately, so
    /// the free experience is what gets tested by default.</summary>
    private static bool DefaultValue =>
#if WINDOWS
        true;
#else
        false;
#endif

    public bool Has(Feature feature) => IsPro;

    public event EventHandler? Changed;
}

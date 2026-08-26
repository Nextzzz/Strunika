namespace Strunika.Mobile.Services;

public interface IHaptics
{
    /// <summary>Light tick — tab snap, segment selection.</summary>
    void Selection();

    /// <summary>Firm tap — tuner locked in tune, chord confirmed.</summary>
    void Success();
}

/// <summary>
/// Thin wrapper over MAUI's HapticFeedback: no-op where unsupported
/// (Windows head) instead of throwing. Static access for controls that
/// live outside DI (<see cref="Controls.PillTabBar"/>).
/// </summary>
public sealed class Haptics : IHaptics
{
    public static readonly Haptics Default = new();

    public void Selection() => Perform(HapticFeedbackType.Click);

    public void Success() => Perform(HapticFeedbackType.LongPress);

    private static void Perform(HapticFeedbackType type)
    {
        try
        {
            if (HapticFeedback.Default.IsSupported)
                HapticFeedback.Default.Perform(type);
        }
        catch (FeatureNotSupportedException) { }
        catch (Exception) { }
    }
}

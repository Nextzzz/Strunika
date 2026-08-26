namespace Strunika.Mobile.Pro;

/// <summary>
/// Single question the UI may ask about Pro. Implementations combine
/// sources (StoreKit entitlement, dev override); <see cref="Changed"/>
/// fires when the answer may have changed so lock badges can refresh.
/// </summary>
public interface IProGate
{
    bool IsPro { get; }

    bool Has(Feature feature);

    event EventHandler? Changed;
}

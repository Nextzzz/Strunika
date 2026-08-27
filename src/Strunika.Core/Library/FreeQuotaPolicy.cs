namespace Strunika.Core.Library;

/// <summary>Persisted quota counters: how many free analyses were used in
/// total, and the local date of the last "one per day" analysis.</summary>
public readonly record struct QuotaState(int Used, DateOnly? LastDailyDate);

/// <summary>
/// Free-tier song analyses (product decision 2026-08-26): 20 lifetime, then
/// one per calendar day in the device's local time. Re-analysing a song
/// that already has a result never counts, and results are never locked
/// away — the policy only gates *new* analyses. Pure so it can be unit
/// tested; storage lives in the app.
/// </summary>
public static class FreeQuotaPolicy
{
    public const int Lifetime = 20;

    public static bool CanAnalyze(QuotaState state, DateOnly today) =>
        state.Used < Lifetime || state.LastDailyDate != today;

    /// <summary>State after one more analysis was started.</summary>
    public static QuotaState Consume(QuotaState state, DateOnly today) =>
        state.Used < Lifetime
            ? state with { Used = state.Used + 1 }
            : state with { Used = state.Used + 1, LastDailyDate = today };

    public static int RemainingLifetime(QuotaState state) => Math.Max(0, Lifetime - state.Used);

    /// <summary>True once the lifetime allowance is spent and the user is on
    /// the one-per-day regime.</summary>
    public static bool IsDaily(QuotaState state) => state.Used >= Lifetime;
}

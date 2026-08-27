using Strunika.Core.Library;
using Strunika.Mobile.Models;

namespace Strunika.Mobile.Pro;

/// <summary>
/// Free-tier analysis counter on top of <see cref="FreeQuotaPolicy"/>.
/// Counters live in the Keychain (SecureStorage) so a reinstall does not
/// reset them; the Windows head falls back to Preferences when the secure
/// store is unavailable (unpackaged app).
/// </summary>
public sealed class FreeQuota
{
    private const string UsedKey = "quota_used", DateKey = "quota_daily_date";
    private readonly IProGate _pro;

    public FreeQuota(IProGate pro) => _pro = pro;

    public async Task<QuotaState> GetAsync()
    {
        int used = int.TryParse(await ReadAsync(UsedKey), out var u) ? u : 0;
        DateOnly? date = DateOnly.TryParseExact(await ReadAsync(DateKey), "yyyy-MM-dd", out var d) ? d : null;
        return new QuotaState(used, date);
    }

    /// <summary>Whether a new analysis of <paramref name="song"/> may start
    /// now. Songs that already have a result are always re-analysable.</summary>
    public async Task<bool> CanStartAsync(Song song)
    {
        if (_pro.Has(Feature.UnlimitedSongs)) return true;
        if (song.Status == SongStatus.Ready) return true;
        return FreeQuotaPolicy.CanAnalyze(await GetAsync(), Today);
    }

    /// <summary>Counts one analysis (no-op for Pro).</summary>
    public async Task ConsumeAsync()
    {
        if (_pro.Has(Feature.UnlimitedSongs)) return;
        var next = FreeQuotaPolicy.Consume(await GetAsync(), Today);
        await WriteAsync(UsedKey, next.Used.ToString());
        if (next.LastDailyDate is { } date)
            await WriteAsync(DateKey, date.ToString("yyyy-MM-dd"));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static async Task<string?> ReadAsync(string key)
    {
        try { return await SecureStorage.Default.GetAsync(key); }
        catch { return Preferences.Default.Get(key, (string?)null); }
    }

    private static async Task WriteAsync(string key, string value)
    {
        try { await SecureStorage.Default.SetAsync(key, value); }
        catch { Preferences.Default.Set(key, value); }
    }
}

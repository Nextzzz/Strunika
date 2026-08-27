using Strunika.Core.Library;

namespace Strunika.Core.Tests;

[TestFixture]
public class FreeQuotaPolicyTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 26);
    private static readonly DateOnly Day2 = new(2026, 8, 27);

    [Test]
    public void TwentyLifetimeAnalysesAreFree()
    {
        var state = new QuotaState(0, null);
        for (int i = 0; i < FreeQuotaPolicy.Lifetime; i++)
        {
            Assert.That(FreeQuotaPolicy.CanAnalyze(state, Day1), Is.True, $"analysis #{i + 1}");
            state = FreeQuotaPolicy.Consume(state, Day1);
        }
        Assert.That(state.Used, Is.EqualTo(20));
        Assert.That(state.LastDailyDate, Is.Null, "lifetime allowance never touches the daily date");
        Assert.That(FreeQuotaPolicy.RemainingLifetime(state), Is.EqualTo(0));
    }

    [Test]
    public void AfterTheAllowance_OnePerDay()
    {
        var state = new QuotaState(20, null);

        Assert.That(FreeQuotaPolicy.CanAnalyze(state, Day1), Is.True, "first daily analysis");
        state = FreeQuotaPolicy.Consume(state, Day1);
        Assert.That(state.LastDailyDate, Is.EqualTo(Day1));

        Assert.That(FreeQuotaPolicy.CanAnalyze(state, Day1), Is.False, "second one the same day");
        Assert.That(FreeQuotaPolicy.CanAnalyze(state, Day2), Is.True, "next day");
        Assert.That(FreeQuotaPolicy.IsDaily(state), Is.True);
    }

    [Test]
    public void RemainingLifetime_NeverNegative()
    {
        Assert.That(FreeQuotaPolicy.RemainingLifetime(new QuotaState(35, Day1)), Is.EqualTo(0));
        Assert.That(FreeQuotaPolicy.RemainingLifetime(new QuotaState(3, null)), Is.EqualTo(17));
    }
}

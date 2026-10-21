using Storefront.Api.Payments;
using Xunit;

namespace Storefront.UnitTests.Payments;

/// <summary>The backoff behind both retry policies (ARCHITECTURE.md §7): exponential, capped, and exact.</summary>
public sealed class RetryScheduleTests
{
    [Fact]
    public void Default_retry_policy_calls_the_provider_at_0_2_6_14_30_and_62_seconds()
    {
        var retry = new RetryOptions();

        var callTimes = new List<TimeSpan> { TimeSpan.Zero };
        for (var callsMade = 1; callsMade < retry.MaxChargeAttempts; callsMade++)
        {
            callTimes.Add(callTimes[^1] + retry.DelayAfter(callsMade));
        }

        Assert.Equal(
            [0, 2, 6, 14, 30, 62],
            callTimes.Select(t => (int)t.TotalSeconds).ToArray());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 32)]
    [InlineData(6, 60)] // 64 s, capped
    [InlineData(20, 60)]
    public void Delay_doubles_and_is_capped(int callsMade, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), new RetryOptions().DelayAfter(callsMade));
    }

    [Fact]
    public void A_huge_step_does_not_overflow()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new RetryOptions().DelayAfter(10_000));
    }

    [Fact]
    public void Multiplier_below_one_never_shrinks_the_delay()
    {
        var retry = new RetryOptions { InitialDelay = TimeSpan.FromSeconds(3), BackoffMultiplier = 0.5 };

        Assert.Equal(TimeSpan.FromSeconds(3), retry.DelayAfter(4));
    }

    [Fact]
    public void Status_checks_start_at_five_seconds_and_back_off_the_same_way()
    {
        var checks = new StatusCheckOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), checks.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(10), checks.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(40), checks.DelayBefore(4));
        Assert.Equal(TimeSpan.FromMinutes(1), checks.DelayBefore(5));
    }
}

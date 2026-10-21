namespace Storefront.Api.Payments;

/// <summary>The <c>Payments</c> configuration section (ARCHITECTURE.md §7). Every value has a sensible default.</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>How long the request thread waits for the provider before treating the outcome as unknown.</summary>
    public TimeSpan GatewayTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public ReconcilerOptions Reconciler { get; init; } = new();

    /// <summary>Retries of a charge the provider refused (nothing charged), with exponential backoff.</summary>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>Status checks of a charge whose outcome is unknown (timeout). These never charge again.</summary>
    public StatusCheckOptions StatusCheck { get; init; } = new();
}

public sealed class ReconcilerOptions
{
    /// <summary>Runs the background loop. The integration tests switch it off and call the reconciler themselves.</summary>
    public bool Enabled { get; init; } = true;

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A payment attempt still <c>Pending</c> after this long was abandoned mid-call (the process died after
    /// saving the order); it is handled like a timeout: look it up, never charge again.
    /// </summary>
    public TimeSpan PendingGracePeriod { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Most attempts processed per pass, so one pass stays short.</summary>
    public int BatchSize { get; init; } = 50;
}

public sealed class RetryOptions
{
    /// <summary>Calls per attempt, the first (inline) call included.</summary>
    public int MaxChargeAttempts { get; init; } = 6;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(2);

    public double BackoffMultiplier { get; init; } = 2;

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Wait before the next call, once <paramref name="callsMade"/> calls have been made (1 or more).</summary>
    public TimeSpan DelayAfter(int callsMade) => Backoff.Delay(InitialDelay, BackoffMultiplier, MaxDelay, callsMade);
}

public sealed class StatusCheckOptions
{
    /// <summary>Checks before the order is handed to a person (<c>NeedsAttention</c>).</summary>
    public int MaxChecks { get; init; } = 6;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    public double BackoffMultiplier { get; init; } = 2;

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Wait before check number <paramref name="checkNumber"/> (1 for the first).</summary>
    public TimeSpan DelayBefore(int checkNumber) => Backoff.Delay(InitialDelay, BackoffMultiplier, MaxDelay, checkNumber);
}

internal static class Backoff
{
    /// <summary><c>initial × multiplier^(step − 1)</c>, capped at <paramref name="max"/>. Defaults give 2, 4, 8, 16, 32, 60 … seconds.</summary>
    public static TimeSpan Delay(TimeSpan initial, double multiplier, TimeSpan max, int step)
    {
        var ticks = initial.Ticks * Math.Pow(Math.Max(multiplier, 1), Math.Max(step, 1) - 1);
        return ticks >= max.Ticks ? max : TimeSpan.FromTicks((long)ticks);
    }
}

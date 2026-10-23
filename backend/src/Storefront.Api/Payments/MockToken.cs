using System.Globalization;

namespace Storefront.Api.Payments;

/// <summary>
/// A mock card token with an optional simulated delay: <c>tok_success@300-800</c> waits a random 300–800 ms,
/// <c>tok_success@500</c> waits exactly 500 ms. The delay is capped at <see cref="MaxDelay"/>.
/// </summary>
public readonly record struct MockToken(string Base, TimeSpan MinDelay, TimeSpan MaxDelayRange)
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    public bool HasDelay => MaxDelayRange > TimeSpan.Zero;

    /// <summary>A delay picked from the range; zero when there is none.</summary>
    public TimeSpan PickDelay() =>
        MaxDelayRange <= MinDelay
            ? MinDelay
            : TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * (MaxDelayRange - MinDelay).TotalMilliseconds + MinDelay.TotalMilliseconds);

    /// <summary>False for a malformed suffix, which the gateway treats like any unknown token.</summary>
    public static bool TryParse(string? token, out MockToken parsed)
    {
        parsed = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var at = token.IndexOf('@');
        if (at < 0)
        {
            parsed = new MockToken(token, TimeSpan.Zero, TimeSpan.Zero);
            return true;
        }

        var baseToken = token[..at];
        var suffix = token[(at + 1)..];
        if (baseToken.Length == 0 || suffix.Length == 0)
        {
            return false;
        }

        var dash = suffix.IndexOf('-');
        long min, max;
        if (dash < 0)
        {
            if (!TryMs(suffix, out min))
            {
                return false;
            }

            max = min;
        }
        else if (!TryMs(suffix[..dash], out min) || !TryMs(suffix[(dash + 1)..], out max) || min > max)
        {
            return false;
        }

        var cap = (long)MaxDelay.TotalMilliseconds;
        parsed = new MockToken(baseToken, TimeSpan.FromMilliseconds(Math.Min(min, cap)), TimeSpan.FromMilliseconds(Math.Min(max, cap)));
        return true;
    }

    private static bool TryMs(string text, out long ms) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ms);
}

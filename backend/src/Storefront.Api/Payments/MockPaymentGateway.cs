using System.Collections.Concurrent;

namespace Storefront.Api.Payments;

/// <summary>
/// The stand-in for a real payment provider (ARCHITECTURE.md §7). The <c>paymentToken</c> picks the outcome,
/// so every path can be tried from Swagger or the UI. Like a real provider it remembers charges by
/// idempotency key: repeating a call never takes the money twice, and <see cref="GetStatusAsync"/> tells the truth.
/// A token may end in <c>@min-max</c> (milliseconds) to simulate a slow provider; see <see cref="MockToken"/>.
/// </summary>
public sealed class MockPaymentGateway : IPaymentGateway
{
    public const string Success = "tok_success";
    public const string Decline = "tok_decline";
    public const string Timeout = "tok_timeout";
    public const string Error = "tok_error";
    public const string Unavailable = "tok_unavailable";
    public const string Flaky = "tok_flaky";

    /// <summary>How many status lookups of a <see cref="Flaky"/> charge fail before the truth comes out.</summary>
    public const int FlakyFailures = 2;

    /// <summary>Charges nobody has touched for this long are forgotten.</summary>
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(1);

    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(5);

    private sealed class Entry(string baseToken, DateTimeOffset now)
    {
        public string BaseToken { get; set; } = baseToken;

        public string? ChargeId { get; set; }

        public int StatusChecks;

        public DateTimeOffset LastSeen { get; set; } = now;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private long _lastCleanupTicks;

    public MockPaymentGateway(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastCleanupTicks = _clock.GetUtcNow().UtcTicks;
    }

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        CleanUp();

        // A malformed suffix is an unknown token.
        if (!MockToken.TryParse(request.PaymentToken, out var token))
        {
            token = new MockToken(request.PaymentToken, TimeSpan.Zero, TimeSpan.Zero);
        }

        var entry = _entries.GetOrAdd(request.IdempotencyKey, _ => new Entry(token.Base, _clock.GetUtcNow()));
        entry.BaseToken = token.Base;
        entry.LastSeen = _clock.GetUtcNow();

        switch (token.Base)
        {
            case Success:
                // The money is taken first, then the provider takes its time to say so.
                Charge(entry);
                await DelayAsync(token, cancellationToken);
                return new ChargeResult(ChargeOutcome.Succeeded, ChargeId: entry.ChargeId);

            case Timeout:
                // The money is taken, but the answer never arrives: the caller's timeout fires first.
                Charge(entry);
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                return new ChargeResult(ChargeOutcome.Unknown);

            case Flaky:
                Charge(entry);
                await DelayAsync(token, cancellationToken);
                return new ChargeResult(ChargeOutcome.Unknown);

            case Decline:
                await DelayAsync(token, cancellationToken);
                return new ChargeResult(ChargeOutcome.Declined, DeclineCode: "insufficient_funds");

            case Error:
                // The provider fails after the call: nothing was charged, but the caller cannot know that.
                await DelayAsync(token, cancellationToken);
                return new ChargeResult(ChargeOutcome.Unknown);

            case Unavailable:
                await DelayAsync(token, cancellationToken);
                return new ChargeResult(ChargeOutcome.Unavailable);

            default:
                return new ChargeResult(ChargeOutcome.Declined, DeclineCode: "invalid_payment_token");
        }
    }

    /// <summary>Status lookups are never delayed.</summary>
    public Task<ChargeStatusResult> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(idempotencyKey, out var entry))
        {
            return Task.FromResult(new ChargeStatusResult(ChargeStatus.NotCharged));
        }

        entry.LastSeen = _clock.GetUtcNow();

        if (entry.BaseToken == Flaky)
        {
            var checks = Interlocked.Increment(ref entry.StatusChecks);
            if (checks <= FlakyFailures)
            {
                return Task.FromResult(new ChargeStatusResult(ChargeStatus.Unknown));
            }
        }

        return Task.FromResult(entry.ChargeId is { } chargeId
            ? new ChargeStatusResult(ChargeStatus.Charged, chargeId)
            : new ChargeStatusResult(ChargeStatus.NotCharged));
    }

    private Task DelayAsync(MockToken token, CancellationToken cancellationToken) =>
        token.HasDelay ? Task.Delay(token.PickDelay(), _clock, cancellationToken) : Task.CompletedTask;

    /// <summary>Takes the money once per idempotency key; a repeat returns the original charge.</summary>
    private static void Charge(Entry entry)
    {
        lock (entry)
        {
            entry.ChargeId ??= $"ch_{Guid.NewGuid():N}";
        }
    }

    /// <summary>At most every <see cref="CleanupEvery"/>, drops entries untouched for <see cref="EntryLifetime"/>.</summary>
    private void CleanUp()
    {
        var now = _clock.GetUtcNow();
        var last = Interlocked.Read(ref _lastCleanupTicks);
        if (now.UtcTicks - last < CleanupEvery.Ticks
            || Interlocked.CompareExchange(ref _lastCleanupTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var (key, entry) in _entries)
        {
            if (now - entry.LastSeen >= EntryLifetime)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}

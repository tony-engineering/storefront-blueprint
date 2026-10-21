using Storefront.Api.Payments;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>
/// Stand-in for the third-party payment provider. It behaves like a real provider in the ways
/// that matter to these tests:
/// <list type="bullet">
///   <item>during an outage every call fails with <see cref="ChargeOutcome.Unavailable"/> and
///   nothing is charged;</item>
///   <item>a charge repeated with the same idempotency key is not charged twice
///   (the original charge is returned);</item>
///   <item>tokens registered with <see cref="Decline"/> are declined; any other token is charged.</item>
/// </list>
/// It records every call and every charge so tests can assert what the customer was actually charged.
/// </summary>
public sealed class ScriptedPaymentGateway(TimeProvider clock) : IPaymentGateway
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _declinedTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordedCharge> _chargesByKey = new(StringComparer.Ordinal);
    private readonly List<GatewayCall> _calls = [];
    private readonly Dictionary<string, bool> _timeoutTokens = new(StringComparer.Ordinal);
    private DateTimeOffset _outageEndsAt = DateTimeOffset.MinValue;

    /// <summary>Every charge call received, in order.</summary>
    public IReadOnlyList<GatewayCall> Calls
    {
        get { lock (_lock) { return [.. _calls]; } }
    }

    /// <summary>Money actually taken: one entry per successful, de-duplicated charge.</summary>
    public IReadOnlyList<RecordedCharge> Charges
    {
        get { lock (_lock) { return [.. _chargesByKey.Values]; } }
    }

    public DateTimeOffset OutageEndsAt
    {
        get { lock (_lock) { return _outageEndsAt; } }
    }

    /// <summary>Makes the provider decline this card.</summary>
    public void Decline(string paymentToken, string declineCode)
    {
        lock (_lock) { _declinedTokens[paymentToken] = declineCode; }
    }

    /// <summary>
    /// Makes charges with this card time out: the caller gets no answer (<see cref="ChargeOutcome.Unknown"/>).
    /// <paramref name="chargeGoesThrough"/> says whether the money was nevertheless taken, which only a status
    /// lookup reveals.
    /// </summary>
    public void Timeout(string paymentToken, bool chargeGoesThrough)
    {
        lock (_lock) { _timeoutTokens[paymentToken] = chargeGoesThrough; }
    }

    /// <summary>Makes the provider unreachable from now (simulated time) for <paramref name="duration"/>.</summary>
    public void StartOutage(TimeSpan duration)
    {
        lock (_lock) { _outageEndsAt = clock.GetUtcNow() + duration; }
    }

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            ChargeResult result;

            if (now < _outageEndsAt)
            {
                result = new ChargeResult(ChargeOutcome.Unavailable);
            }
            else if (_chargesByKey.TryGetValue(request.IdempotencyKey, out var existing))
            {
                // Same idempotency key: report the original charge, take no new money.
                result = new ChargeResult(ChargeOutcome.Succeeded, ChargeId: existing.ChargeId);
            }
            else if (_declinedTokens.TryGetValue(request.PaymentToken, out var declineCode))
            {
                result = new ChargeResult(ChargeOutcome.Declined, DeclineCode: declineCode);
            }
            else
            {
                var timesOut = _timeoutTokens.TryGetValue(request.PaymentToken, out var chargeGoesThrough);
                var chargeId = "";
                if (!timesOut || chargeGoesThrough)
                {
                    var charge = new RecordedCharge(
                        ChargeId: $"ch_{_chargesByKey.Count + 1}",
                        IdempotencyKey: request.IdempotencyKey,
                        OrderId: request.OrderId,
                        PaymentToken: request.PaymentToken,
                        AmountMinor: request.AmountMinor,
                        Currency: request.Currency,
                        At: now);
                    _chargesByKey.Add(request.IdempotencyKey, charge);
                    chargeId = charge.ChargeId;
                }

                // A timeout looks the same to the caller whether or not the money was taken.
                result = timesOut
                    ? new ChargeResult(ChargeOutcome.Unknown)
                    : new ChargeResult(ChargeOutcome.Succeeded, ChargeId: chargeId);
            }

            _calls.Add(new GatewayCall(now, request.IdempotencyKey, request.OrderId, request.PaymentToken, result.Outcome));
            return Task.FromResult(result);
        }
    }

    public Task<ChargeStatusResult> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (clock.GetUtcNow() < _outageEndsAt)
            {
                return Task.FromResult(new ChargeStatusResult(ChargeStatus.Unknown));
            }

            return Task.FromResult(_chargesByKey.TryGetValue(idempotencyKey, out var charge)
                ? new ChargeStatusResult(ChargeStatus.Charged, charge.ChargeId)
                : new ChargeStatusResult(ChargeStatus.NotCharged));
        }
    }
}

public sealed record GatewayCall(
    DateTimeOffset At,
    string IdempotencyKey,
    Guid OrderId,
    string PaymentToken,
    ChargeOutcome Outcome);

public sealed record RecordedCharge(
    string ChargeId,
    string IdempotencyKey,
    Guid OrderId,
    string PaymentToken,
    long AmountMinor,
    string Currency,
    DateTimeOffset At);

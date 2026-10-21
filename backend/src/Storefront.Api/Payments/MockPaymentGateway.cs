using System.Collections.Concurrent;

namespace Storefront.Api.Payments;

/// <summary>
/// The stand-in for a real payment provider (ARCHITECTURE.md §7). The <c>paymentToken</c> picks the outcome,
/// so every path can be tried from Swagger or the UI. Like a real provider it remembers charges by
/// idempotency key: repeating a call never takes the money twice, and <see cref="GetStatusAsync"/> tells the truth.
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

    private readonly ConcurrentDictionary<string, string> _chargeIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _statusChecks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        _tokens[request.IdempotencyKey] = request.PaymentToken;

        switch (request.PaymentToken)
        {
            case Success:
                return new ChargeResult(ChargeOutcome.Succeeded, ChargeId: Charge(request.IdempotencyKey));

            case Decline:
                return new ChargeResult(ChargeOutcome.Declined, DeclineCode: "insufficient_funds");

            case Timeout:
                // The money is taken, but the answer never arrives: the caller's timeout fires first.
                Charge(request.IdempotencyKey);
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                return new ChargeResult(ChargeOutcome.Unknown);

            case Error:
                // The provider fails after the call: nothing was charged, but the caller cannot know that.
                return new ChargeResult(ChargeOutcome.Unknown);

            case Unavailable:
                return new ChargeResult(ChargeOutcome.Unavailable);

            case Flaky:
                Charge(request.IdempotencyKey);
                return new ChargeResult(ChargeOutcome.Unknown);

            default:
                return new ChargeResult(ChargeOutcome.Declined, DeclineCode: "invalid_payment_token");
        }
    }

    public Task<ChargeStatusResult> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(idempotencyKey, out var token) && token == Flaky)
        {
            var checks = _statusChecks.AddOrUpdate(idempotencyKey, 1, (_, count) => count + 1);
            if (checks <= FlakyFailures)
            {
                return Task.FromResult(new ChargeStatusResult(ChargeStatus.Unknown));
            }
        }

        return Task.FromResult(_chargeIds.TryGetValue(idempotencyKey, out var chargeId)
            ? new ChargeStatusResult(ChargeStatus.Charged, chargeId)
            : new ChargeStatusResult(ChargeStatus.NotCharged));
    }

    /// <summary>Takes the money once per idempotency key; a repeat returns the original charge.</summary>
    private string Charge(string idempotencyKey) =>
        _chargeIds.GetOrAdd(idempotencyKey, _ => $"ch_{Guid.NewGuid():N}");
}

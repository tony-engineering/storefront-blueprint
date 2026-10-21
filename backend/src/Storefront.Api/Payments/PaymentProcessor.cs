using Microsoft.Extensions.Options;
using Storefront.Api.Features.Orders;

namespace Storefront.Api.Payments;

/// <summary>
/// Talks to the payment provider and moves the order and the payment attempt to the state the answer implies
/// (ARCHITECTURE.md §7). Used by checkout for the first call and by the reconciler for retries and status checks.
/// It changes the entities but never saves them: the caller owns the transaction.
/// </summary>
public sealed class PaymentProcessor(
    IPaymentGateway gateway,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentProcessor> logger)
{
    /// <summary>
    /// Calls the provider once to charge <paramref name="attempt"/>. The attempt ID is the idempotency key, so a
    /// repeat of the same attempt can never charge twice. A timeout or any other failure means "unknown": the
    /// charge may have gone through, so it is only ever looked up afterwards, never repeated.
    /// </summary>
    public async Task ChargeAsync(Order order, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        attempt.RecordChargeCall();

        var request = new ChargeRequest(
            IdempotencyKey: attempt.Id.ToString("D"),
            OrderId: order.Id,
            AmountMinor: order.Total.AmountMinor,
            Currency: order.Total.Currency,
            PaymentToken: attempt.PaymentToken);

        ChargeResult result;
        try
        {
            // Wall-clock timeout on purpose: it limits real waiting, not business time.
            using var timeout = new CancellationTokenSource(settings.GatewayTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            result = await gateway.ChargeAsync(request, linked.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Charge call for attempt {AttemptId} of order {OrderId} got no answer.", attempt.Id, order.Id);
            result = new ChargeResult(ChargeOutcome.Unknown);
        }

        ApplyCharge(order, attempt, result, settings);
    }

    /// <summary>Asks the provider what became of a charge whose outcome was unknown. Never charges.</summary>
    public async Task CheckStatusAsync(Order order, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        attempt.RecordStatusCheck();

        ChargeStatusResult status;
        try
        {
            using var timeout = new CancellationTokenSource(settings.GatewayTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            status = await gateway.GetStatusAsync(attempt.Id.ToString("D"), linked.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Status check for attempt {AttemptId} of order {OrderId} got no answer.", attempt.Id, order.Id);
            status = new ChargeStatusResult(ChargeStatus.Unknown);
        }

        var now = clock.GetUtcNow();
        switch (status.Status)
        {
            case ChargeStatus.Charged:
                attempt.MarkSucceeded(status.ChargeId, now);
                order.MarkPaid(now);
                break;

            case ChargeStatus.NotCharged:
                attempt.MarkNotProcessed(now);
                order.MarkFailed(PaymentAttempt.NotProcessedReason, now);
                break;

            default:
                if (attempt.StatusChecks >= settings.StatusCheck.MaxChecks)
                {
                    logger.LogError(
                        "Order {OrderId}: {Checks} status checks could not tell whether the customer was charged. A person must look.",
                        order.Id, attempt.StatusChecks);
                    attempt.GiveUpWaiting(now);
                    order.MarkNeedsAttention(now);
                }
                else
                {
                    attempt.ScheduleStatusCheck(now + settings.StatusCheck.DelayBefore(attempt.StatusChecks + 1), now);
                }

                break;
        }
    }

    private void ApplyCharge(Order order, PaymentAttempt attempt, ChargeResult result, PaymentOptions settings)
    {
        var now = clock.GetUtcNow();
        switch (result.Outcome)
        {
            case ChargeOutcome.Succeeded:
                attempt.MarkSucceeded(result.ChargeId, now);
                order.MarkPaid(now);
                break;

            case ChargeOutcome.Declined:
                var code = string.IsNullOrWhiteSpace(result.DeclineCode) ? "card_declined" : result.DeclineCode;
                attempt.MarkDeclined(code, now);
                order.MarkFailed(code, now);
                break;

            case ChargeOutcome.Unavailable:
                // The provider did not take the call, so nothing was charged and retrying the same attempt is safe.
                if (order.Status == OrderStatus.Pending)
                {
                    order.MarkRetrying(now);
                }

                if (attempt.ChargeCalls >= settings.Retry.MaxChargeAttempts)
                {
                    attempt.MarkProviderUnavailable(now);
                    order.MarkUnavailable(now);
                }
                else
                {
                    attempt.ScheduleRetry(now + settings.Retry.DelayAfter(attempt.ChargeCalls), now);
                }

                break;

            default:
                // Timeout or an unexplained failure: the charge may have gone through. Look it up later.
                attempt.ScheduleStatusCheck(now + settings.StatusCheck.DelayBefore(1), now);
                order.MarkUnknown(now);
                break;
        }
    }
}

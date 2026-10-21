namespace Storefront.Api.Features.Orders;

/// <summary>Where one payment attempt stands. An order can have several attempts (another card, a later try).</summary>
public enum PaymentAttemptStatus
{
    /// <summary>Saved, provider not answered yet.</summary>
    Pending,

    Succeeded,

    /// <summary>The bank said no. Final: never retried automatically.</summary>
    Declined,

    /// <summary>The provider refused the call (nothing charged); the same attempt is retried with backoff.</summary>
    Retrying,

    /// <summary>Retries ran out and the provider never took the call. Nothing was charged.</summary>
    ProviderUnavailable,

    /// <summary>The call timed out, so the charge may have gone through. Only status-checked, never re-charged.</summary>
    Unknown,

    /// <summary>A status check found that the provider never processed the charge.</summary>
    Failed,
}

/// <summary>
/// One try at taking payment for an order. Its <see cref="Id"/> is the idempotency key sent to the provider:
/// every retry of this attempt carries the same key, so it can never charge twice, while a new attempt
/// (another card) is a genuinely new charge (ARCHITECTURE.md §4).
/// </summary>
public sealed class PaymentAttempt
{
    public const string NotProcessedReason = "payment_not_processed";

    // For EF Core.
    private PaymentAttempt()
    {
        PaymentToken = null!;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>1 for the first attempt on an order, then 2, 3 …</summary>
    public int Number { get; private set; }

    public string PaymentToken { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Calls made to the provider to charge, the first (inline) call included.</summary>
    public int ChargeCalls { get; private set; }

    /// <summary>Status lookups made while the outcome was unknown.</summary>
    public int StatusChecks { get; private set; }

    /// <summary>When the reconciler should next retry the charge or check the status; null when nothing is scheduled.</summary>
    public DateTimeOffset? NextCallAt { get; private set; }

    public string? ChargeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal static PaymentAttempt Create(Guid orderId, int number, string paymentToken, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentToken);

        return new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Number = number,
            PaymentToken = paymentToken,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Counts a call to the provider. Called just before each charge call.</summary>
    public void RecordChargeCall() => ChargeCalls++;

    public void MarkSucceeded(string? chargeId, DateTimeOffset now) =>
        Settle(PaymentAttemptStatus.Succeeded, failureReason: null, chargeId, now);

    public void MarkDeclined(string declineCode, DateTimeOffset now) =>
        Settle(PaymentAttemptStatus.Declined, declineCode, chargeId: null, now);

    public void MarkProviderUnavailable(DateTimeOffset now) =>
        Settle(PaymentAttemptStatus.ProviderUnavailable, Order.ProviderUnavailableReason, chargeId: null, now);

    public void MarkNotProcessed(DateTimeOffset now) =>
        Settle(PaymentAttemptStatus.Failed, NotProcessedReason, chargeId: null, now);

    /// <summary>The provider refused the call: try this same attempt again at <paramref name="retryAt"/>.</summary>
    public void ScheduleRetry(DateTimeOffset retryAt, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Retrying;
        NextCallAt = retryAt;
        UpdatedAt = now;
    }

    /// <summary>The outcome is unknown: look the charge up at <paramref name="checkAt"/>. Never charge again.</summary>
    public void ScheduleStatusCheck(DateTimeOffset checkAt, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Unknown;
        NextCallAt = checkAt;
        UpdatedAt = now;
    }

    public void RecordStatusCheck() => StatusChecks++;

    /// <summary>The attempt has an answer (or has been given up on): nothing more is scheduled.</summary>
    public void GiveUpWaiting(DateTimeOffset now)
    {
        NextCallAt = null;
        UpdatedAt = now;
    }

    private void Settle(PaymentAttemptStatus status, string? failureReason, string? chargeId, DateTimeOffset now)
    {
        Status = status;
        FailureReason = failureReason;
        ChargeId = chargeId ?? ChargeId;
        NextCallAt = null;
        UpdatedAt = now;
    }
}

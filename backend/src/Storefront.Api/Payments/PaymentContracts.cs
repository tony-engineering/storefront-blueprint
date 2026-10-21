namespace Storefront.Api.Payments;

// Contracts only; no implementation yet. The integration tests replace IPaymentGateway
// with a scripted fake and drive IPaymentReconciler directly.

public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken);

    Task<ChargeStatusResult> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken);
}

/// <param name="IdempotencyKey">The payment attempt's ID: the same on every retry of one attempt.</param>
public sealed record ChargeRequest(string IdempotencyKey, Guid OrderId, long AmountMinor, string Currency, string PaymentToken);

public enum ChargeOutcome
{
    Succeeded,
    Declined,

    /// <summary>The provider definitely did not process the charge (connection refused, HTTP 503). Safe to retry.</summary>
    Unavailable,

    /// <summary>The charge may have been processed (timeout). Never re-charged, only status-checked.</summary>
    Unknown,
}

public sealed record ChargeResult(ChargeOutcome Outcome, string? ChargeId = null, string? DeclineCode = null);

public enum ChargeStatus
{
    Charged,
    NotCharged,
    Unknown,
}

public sealed record ChargeStatusResult(ChargeStatus Status, string? ChargeId = null);

public interface IPaymentReconciler
{
    /// <summary>Processes every payment attempt whose next retry or status check is due, then returns.</summary>
    Task RunOnceAsync(CancellationToken cancellationToken);
}

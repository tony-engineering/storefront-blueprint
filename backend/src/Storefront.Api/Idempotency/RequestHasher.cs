using Storefront.Api.Features.Orders;

namespace Storefront.Api.Idempotency;

/// <summary>
/// Fingerprints a request body so a repeated <c>Idempotency-Key</c> can be classed as
/// "same request" (replay the stored response) or "different request" (422). ARCHITECTURE.md §6.
/// </summary>
/// <remarks>
/// Contract only; the implementation is still to be written (test-first, see Storefront.UnitTests).
/// The hash is SHA-256, as 64 lower-case hex characters, over a canonical form of the parsed request:
/// lines merged by product and sorted by product ID, so JSON formatting, property order and line order
/// never matter. The canonical form also names the operation (and, for a payment, the order), so the
/// same key sent to two different endpoints or for two different orders never counts as a replay.
/// </remarks>
public static class RequestHasher
{
    /// <summary>Hash of a <c>POST /api/orders</c> body.</summary>
    public static string Hash(CreateOrderRequest request) =>
        throw new NotImplementedException();

    /// <summary>Hash of a <c>POST /api/orders/{orderId}/payments</c> body.</summary>
    public static string Hash(Guid orderId, RetryPaymentRequest request) =>
        throw new NotImplementedException();
}

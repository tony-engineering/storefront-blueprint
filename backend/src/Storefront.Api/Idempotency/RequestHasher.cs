using System.Security.Cryptography;
using System.Text.Json;
using Storefront.Api.Features.Orders;

namespace Storefront.Api.Idempotency;

/// <summary>
/// Fingerprints a request body so a repeated <c>Idempotency-Key</c> can be classed as
/// "same request" (replay the stored response) or "different request" (422). ARCHITECTURE.md §6.
/// </summary>
/// <remarks>
/// The hash is SHA-256, as 64 lower-case hex characters, over a canonical form of the parsed request:
/// lines merged by product and sorted by product ID, so JSON formatting, property order and line order
/// never matter. The canonical form also names the operation (and, for a payment, the order), so the
/// same key sent to two different endpoints or for two different orders never counts as a replay.
/// It is written as JSON, so field boundaries are explicit and values cannot be shifted between fields.
/// </remarks>
public static class RequestHasher
{
    /// <summary>Hash of a <c>POST /api/orders</c> body.</summary>
    public static string Hash(CreateOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HashCanonical("create-order", orderId: null, request.PaymentToken, request.Lines);
    }

    /// <summary>Hash of a <c>POST /api/orders/{orderId}/payments</c> body.</summary>
    public static string Hash(Guid orderId, RetryPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HashCanonical("pay-order", orderId, request.PaymentToken, lines: null);
    }

    private static string HashCanonical(
        string operation, Guid? orderId, string? paymentToken, IReadOnlyList<OrderLineRequest>? lines)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("operation", operation);

            if (orderId is { } id)
            {
                json.WriteString("orderId", id.ToString("D"));
            }

            json.WriteStartArray("lines");
            foreach (var (productId, quantity) in Merge(lines))
            {
                json.WriteStartObject();
                json.WriteString("productId", productId);
                json.WriteNumber("quantity", quantity);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteString("paymentToken", paymentToken);
            json.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>One entry per product (quantities added up), sorted by product ID.</summary>
    private static IEnumerable<(string ProductId, long Quantity)> Merge(IReadOnlyList<OrderLineRequest>? lines) =>
        (lines ?? [])
            .GroupBy(line => line.ProductId.ToString("D"))
            .Select(group => (ProductId: group.Key, Quantity: group.Sum(line => (long)line.Quantity)))
            .OrderBy(entry => entry.ProductId, StringComparer.Ordinal);
}

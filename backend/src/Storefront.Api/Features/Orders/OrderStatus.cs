namespace Storefront.Api.Features.Orders;

/// <summary>Order lifecycle; the legal moves between these are listed in ARCHITECTURE.md §4.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    PaymentFailed,
    PaymentRetrying,
    PaymentUnknown,
    PaymentUnavailable,
    NeedsAttention,
}

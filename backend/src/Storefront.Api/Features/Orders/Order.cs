using Storefront.Api.Common;

namespace Storefront.Api.Features.Orders;

/// <summary>
/// An order and its state machine (ARCHITECTURE.md §4). Every status change goes through one of the
/// <c>Mark…</c> methods, which throw <see cref="InvalidOrderTransitionException"/> on an illegal move
/// and leave the order untouched, so no code path can, for example, mark a paid order as failed.
/// </summary>
/// <remarks>
/// Contract only; the implementation is still to be written (test-first, see Storefront.UnitTests).
/// Time is passed in by the caller (who reads the injected <see cref="TimeProvider"/>), so the entity stays pure.
/// </remarks>
public sealed class Order
{
    public const string ProviderUnavailableReason = "payment_provider_unavailable";

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>Sum of the line totals, computed on the server; never taken from the client.</summary>
    public Money Total { get; private set; }

    /// <summary>The decline code or <see cref="ProviderUnavailableReason"/>; null unless the payment failed.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines { get; private set; } = [];

    /// <summary>
    /// A new <see cref="OrderStatus.Pending"/> order. Throws <see cref="ArgumentException"/> without lines
    /// and <see cref="CurrencyMismatchException"/> when lines are priced in different currencies.
    /// </summary>
    public static Order Create(IReadOnlyList<OrderLine> lines, DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>Pending / PaymentRetrying / PaymentUnknown → Paid. Clears any failure reason.</summary>
    public void MarkPaid(DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>Pending / PaymentRetrying / PaymentUnknown → PaymentFailed, with a non-empty reason.</summary>
    public void MarkFailed(string reason, DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>Pending → PaymentRetrying: the provider refused the call, nothing was charged.</summary>
    public void MarkRetrying(DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>Pending / PaymentRetrying → PaymentUnknown: the call timed out, the charge may have gone through.</summary>
    public void MarkUnknown(DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>PaymentRetrying → PaymentUnavailable, reason <see cref="ProviderUnavailableReason"/>.</summary>
    public void MarkUnavailable(DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>PaymentUnknown → NeedsAttention: status checks exhausted, a person must look.</summary>
    public void MarkNeedsAttention(DateTimeOffset now) =>
        throw new NotImplementedException();

    /// <summary>
    /// PaymentFailed / PaymentUnavailable → Pending: the customer starts a new payment attempt
    /// (for example with another card). Clears the failure reason; the total is unchanged.
    /// </summary>
    public void StartNewPayment(DateTimeOffset now) =>
        throw new NotImplementedException();
}

/// <summary>A line as it was at purchase time: name and unit price are a snapshot, not a reference.</summary>
public sealed record OrderLine
{
    /// <remarks>Throws <see cref="ArgumentOutOfRangeException"/> for a quantity below 1.</remarks>
    public OrderLine(Guid productId, string productName, Money unitPrice, int quantity) =>
        throw new NotImplementedException();

    public Guid ProductId { get; }

    public string ProductName { get; } = "";

    public Money UnitPrice { get; }

    public int Quantity { get; }

    /// <summary>Unit price × quantity.</summary>
    public Money LineTotal => throw new NotImplementedException();
}

public sealed class InvalidOrderTransitionException(OrderStatus from, OrderStatus to)
    : InvalidOperationException($"An order cannot move from {from} to {to}.")
{
    public OrderStatus From { get; } = from;

    public OrderStatus To { get; } = to;
}

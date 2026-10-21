using Storefront.Api.Common;

namespace Storefront.Api.Features.Orders;

/// <summary>
/// An order and its state machine (ARCHITECTURE.md §4). Every status change goes through one of the
/// <c>Mark…</c> methods, which throw <see cref="InvalidOrderTransitionException"/> on an illegal move
/// and leave the order untouched, so no code path can, for example, mark a paid order as failed.
/// </summary>
/// <remarks>
/// Time is passed in by the caller (who reads the injected <see cref="TimeProvider"/>), so the entity stays pure.
/// </remarks>
public sealed class Order
{
    public const string ProviderUnavailableReason = "payment_provider_unavailable";

    /// <summary>The diagram in ARCHITECTURE.md §4, edge by edge. Anything not listed is illegal.</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> LegalTransitions =
    [
        (OrderStatus.Pending, OrderStatus.Paid),
        (OrderStatus.Pending, OrderStatus.PaymentFailed),
        (OrderStatus.Pending, OrderStatus.PaymentRetrying),
        (OrderStatus.Pending, OrderStatus.PaymentUnknown),

        (OrderStatus.PaymentRetrying, OrderStatus.Paid),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentFailed),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentUnknown),
        (OrderStatus.PaymentRetrying, OrderStatus.PaymentUnavailable),

        (OrderStatus.PaymentUnknown, OrderStatus.Paid),
        (OrderStatus.PaymentUnknown, OrderStatus.PaymentFailed),
        (OrderStatus.PaymentUnknown, OrderStatus.NeedsAttention),

        (OrderStatus.PaymentFailed, OrderStatus.Pending),
        (OrderStatus.PaymentUnavailable, OrderStatus.Pending),
    ];

    private readonly List<OrderLine> _lines = [];
    private readonly List<PaymentAttempt> _payments = [];

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

    /// <summary>
    /// Optimistic concurrency token, mapped to PostgreSQL's <c>xmin</c> system column. If two writers
    /// change the same order, the second save fails instead of silently overwriting the first.
    /// </summary>
    public uint Version { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>Every payment attempt, oldest first.</summary>
    public IReadOnlyList<PaymentAttempt> Payments => _payments;

    /// <summary>True only once the money has been taken. Everything else is "not (known to be) charged".</summary>
    public bool Charged => Status == OrderStatus.Paid;

    /// <summary>
    /// A new <see cref="OrderStatus.Pending"/> order. Throws <see cref="ArgumentException"/> without lines
    /// or with the same product twice, and <see cref="CurrencyMismatchException"/> when lines are priced in
    /// different currencies. <paramref name="id"/> is for seed data that needs a well-known ID; leave it out otherwise.
    /// </summary>
    public static Order Create(IReadOnlyList<OrderLine> lines, DateTimeOffset now, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new ArgumentException("A product may appear on an order only once.", nameof(lines));
        }

        var total = Money.Sum(lines.Select(l => l.LineTotal), lines[0].UnitPrice.Currency);

        var order = new Order
        {
            Id = id ?? Guid.CreateVersion7(),
            Status = OrderStatus.Pending,
            Total = total,
            CreatedAt = now,
            UpdatedAt = now,
        };

        for (var position = 0; position < lines.Count; position++)
        {
            lines[position].PlaceAt(position);
            order._lines.Add(lines[position]);
        }

        return order;
    }

    /// <summary>
    /// Records a new payment attempt. Only a <see cref="OrderStatus.Pending"/> order can have one in flight;
    /// anything else throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public PaymentAttempt AddPaymentAttempt(string paymentToken, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"A payment cannot be started while the order is {Status}.");
        }

        var attempt = PaymentAttempt.Create(Id, _payments.Count + 1, paymentToken, now);
        _payments.Add(attempt);
        return attempt;
    }

    /// <summary>Pending / PaymentRetrying / PaymentUnknown → Paid. Clears any failure reason.</summary>
    public void MarkPaid(DateTimeOffset now)
    {
        MoveTo(OrderStatus.Paid, now);
        FailureReason = null;
    }

    /// <summary>Pending / PaymentRetrying / PaymentUnknown → PaymentFailed, with a non-empty reason.</summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        MoveTo(OrderStatus.PaymentFailed, now);
        FailureReason = reason;
    }

    /// <summary>Pending → PaymentRetrying: the provider refused the call, nothing was charged.</summary>
    public void MarkRetrying(DateTimeOffset now) => MoveTo(OrderStatus.PaymentRetrying, now);

    /// <summary>Pending / PaymentRetrying → PaymentUnknown: the call timed out, the charge may have gone through.</summary>
    public void MarkUnknown(DateTimeOffset now) => MoveTo(OrderStatus.PaymentUnknown, now);

    /// <summary>PaymentRetrying → PaymentUnavailable, reason <see cref="ProviderUnavailableReason"/>.</summary>
    public void MarkUnavailable(DateTimeOffset now)
    {
        MoveTo(OrderStatus.PaymentUnavailable, now);
        FailureReason = ProviderUnavailableReason;
    }

    /// <summary>PaymentUnknown → NeedsAttention: status checks exhausted, a person must look.</summary>
    public void MarkNeedsAttention(DateTimeOffset now) => MoveTo(OrderStatus.NeedsAttention, now);

    /// <summary>
    /// PaymentFailed / PaymentUnavailable → Pending: the customer starts a new payment attempt
    /// (for example with another card). Clears the failure reason; the total is unchanged.
    /// </summary>
    public void StartNewPayment(DateTimeOffset now)
    {
        MoveTo(OrderStatus.Pending, now);
        FailureReason = null;
    }

    private void MoveTo(OrderStatus to, DateTimeOffset now)
    {
        if (!LegalTransitions.Contains((Status, to)))
        {
            throw new InvalidOrderTransitionException(Status, to);
        }

        Status = to;
        UpdatedAt = now;
    }
}

/// <summary>A line as it was at purchase time: name and unit price are a snapshot, not a reference.</summary>
public sealed class OrderLine
{
    // For EF Core.
    private OrderLine()
    {
        ProductName = null!;
    }

    /// <remarks>Throws <see cref="ArgumentOutOfRangeException"/> for a quantity below 1.</remarks>
    public OrderLine(Guid productId, string productName, Money unitPrice, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    /// <summary>Position on the order, so lines come back in the order they were placed.</summary>
    public int Position { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public Money UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Unit price × quantity.</summary>
    public Money LineTotal => UnitPrice * Quantity;

    internal void PlaceAt(int position) => Position = position;
}

public sealed class InvalidOrderTransitionException(OrderStatus from, OrderStatus to)
    : InvalidOperationException($"An order cannot move from {from} to {to}.")
{
    public OrderStatus From { get; } = from;

    public OrderStatus To { get; } = to;
}

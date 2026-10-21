using System.ComponentModel;

namespace Storefront.Api.Features.Orders;

/// <summary>An order as the client sees it (camelCase JSON, enums as strings).</summary>
public sealed record OrderDto(
    Guid Id,
    OrderStatus Status,
    [property: Description("True only when the order is Paid. Every other status means the customer has not been (confirmed) charged.")] bool Charged,
    [property: Description("The decline code, or `payment_provider_unavailable`; null unless the payment failed.")] string? FailureReason,
    MoneyDto Total,
    IReadOnlyList<OrderLineDto> Lines,
    [property: Description("Every payment attempt, oldest first.")] IReadOnlyList<PaymentAttemptDto> Payments)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Status,
        order.Charged,
        order.FailureReason,
        MoneyDto.From(order.Total),
        [.. order.Lines.OrderBy(l => l.Position).Select(OrderLineDto.From)],
        [.. order.Payments.OrderBy(p => p.Number).Select(PaymentAttemptDto.From)]);
}

public sealed record OrderLineDto(Guid ProductId, string ProductName, MoneyDto UnitPrice, int Quantity)
{
    public static OrderLineDto From(OrderLine line) =>
        new(line.ProductId, line.ProductName, MoneyDto.From(line.UnitPrice), line.Quantity);
}

public sealed record PaymentAttemptDto(int Number, PaymentAttemptStatus Status, string? FailureReason)
{
    public static PaymentAttemptDto From(PaymentAttempt attempt) =>
        new(attempt.Number, attempt.Status, attempt.FailureReason);
}

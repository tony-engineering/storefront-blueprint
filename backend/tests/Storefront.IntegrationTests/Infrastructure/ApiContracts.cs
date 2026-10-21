namespace Storefront.IntegrationTests.Infrastructure;

// The wire contract, declared independently of the API's own types on purpose:
// if the API renames a property, these tests break instead of silently following along,
// the same way the Angular client would.

public sealed record MoneyDto(long AmountMinor, string Currency);

public sealed record ProductDto(Guid Id, string Name, string? Description, MoneyDto Price);

public sealed record OrderLineRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Lines, string PaymentToken);

public sealed record RetryPaymentRequest(string PaymentToken);

public sealed record OrderLineDto(Guid ProductId, string ProductName, MoneyDto UnitPrice, int Quantity);

public sealed record PaymentAttemptDto(int Number, string Status, string? FailureReason);

public sealed record OrderDto(
    Guid Id,
    string Status,
    MoneyDto Total,
    IReadOnlyList<OrderLineDto> Lines,
    bool Charged,
    string? FailureReason,
    IReadOnlyList<PaymentAttemptDto> Payments);

public static class OrderStatuses
{
    public const string Paid = "Paid";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRetrying = "PaymentRetrying";
    public const string PaymentUnavailable = "PaymentUnavailable";
}

public static class PaymentAttemptStatuses
{
    public const string Succeeded = "Succeeded";
    public const string Declined = "Declined";
    public const string Retrying = "Retrying";
    public const string ProviderUnavailable = "ProviderUnavailable";
}

public static class FailureReasons
{
    public const string ProviderUnavailable = "payment_provider_unavailable";
}

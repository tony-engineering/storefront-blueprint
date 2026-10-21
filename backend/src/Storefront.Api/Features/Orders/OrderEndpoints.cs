using Microsoft.AspNetCore.Mvc;

namespace Storefront.Api.Features.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapPost("", (
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CreateOrderRequest request) =>
            NotImplementedYet.Problem("Creating an order"));

        orders.MapGet("/{id:guid}", (Guid id) =>
            NotImplementedYet.Problem("Retrieving an order"));

        orders.MapPost("/{id:guid}/payments", (
            Guid id,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            RetryPaymentRequest request) =>
            NotImplementedYet.Problem("Paying an existing order"));

        return app;
    }
}

public sealed record OrderLineRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Lines, string PaymentToken);

public sealed record RetryPaymentRequest(string PaymentToken);

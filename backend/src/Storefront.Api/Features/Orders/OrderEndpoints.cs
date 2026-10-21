using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;

namespace Storefront.Api.Features.Orders;

public static class OrderEndpoints
{
    private const string IdempotencyKeyDescription =
        "A UUID identifying this attempt, e.g. one generated when the checkout page opens. " +
        "Reuse it to retry safely; use a new one for a new attempt.";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders")
            .WithTags("Orders");

        orders.MapPost("", (
                [FromHeader(Name = "Idempotency-Key"), Description(IdempotencyKeyDescription)] string? idempotencyKey,
                CreateOrderRequest request) =>
                NotImplementedYet.Problem("Creating an order"))
            .WithName("CreateOrder")
            .WithSummary("Create an order and pay for it")
            .WithDescription(
                "Prices are computed by the server from the product IDs. " +
                "201 when the payment outcome is known (paid or declined); 202 while it is still being resolved " +
                "(poll `GET /api/orders/{id}`). The `paymentToken` picks the mock payment outcome.");

        orders.MapGet("/{id:guid}", (Guid id) =>
                NotImplementedYet.Problem("Retrieving an order"))
            .WithName("GetOrder")
            .WithSummary("Get an order")
            .WithDescription("Returns the order, its lines and its payment attempts. 404 if the ID is unknown.");

        orders.MapPost("/{id:guid}/payments", (
                Guid id,
                [FromHeader(Name = "Idempotency-Key"), Description(IdempotencyKeyDescription)] string? idempotencyKey,
                RetryPaymentRequest request) =>
                NotImplementedYet.Problem("Paying an existing order"))
            .WithName("RetryPayment")
            .WithSummary("Pay again for an existing order")
            .WithDescription(
                "Starts a new payment attempt, e.g. with another card. " +
                "Only allowed when the order is `PaymentFailed` or `PaymentUnavailable`; otherwise 409.");

        return app;
    }
}

public sealed record OrderLineRequest(
    Guid ProductId,
    [property: Description("1 to 100."), DefaultValue(1)] int Quantity);

public sealed record CreateOrderRequest(
    [property: Description("1 to 50 lines, each product at most once.")] IReadOnlyList<OrderLineRequest> Lines,
    [property: Description(PaymentTokens.Description), DefaultValue("tok_success")] string PaymentToken);

public sealed record RetryPaymentRequest(
    [property: Description(PaymentTokens.Description), DefaultValue("tok_success")] string PaymentToken);

internal static class PaymentTokens
{
    public const string Description =
        "Mock card token that picks the payment outcome: tok_success, tok_decline, tok_timeout, " +
        "tok_error, tok_unavailable or tok_flaky. See the API description for what each one does.";
}

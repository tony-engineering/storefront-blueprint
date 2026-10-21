using System.ComponentModel;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Storefront.Api.Idempotency;

namespace Storefront.Api.Features.Orders;

public static class OrderEndpoints
{
    private const string IdempotencyKeyDescription =
        "A UUID identifying this attempt, e.g. one generated when the checkout page opens. " +
        "Reuse it to retry safely; use a new one for a new attempt.";

    public static IServiceCollection AddOrders(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();
        services.AddScoped<IValidator<RetryPaymentRequest>, RetryPaymentRequestValidator>();
        services.AddScoped<IdempotencyExecutor>();
        services.AddScoped<OrderService>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders")
            .WithTags("Orders");

        orders.MapPost("", CreateOrderAsync)
            .WithName("CreateOrder")
            .WithSummary("Create an order and pay for it")
            .WithDescription(
                "Prices are computed by the server from the product IDs. " +
                "201 when the payment outcome is known (paid or declined); 202 while it is still being resolved " +
                "(poll `GET /api/orders/{id}`). The `paymentToken` picks the mock payment outcome.")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces<OrderDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapGet("/{id:guid}", GetOrderAsync)
            .WithName("GetOrder")
            .WithSummary("Get an order")
            .WithDescription("Returns the order, its lines and its payment attempts. 404 if the ID is unknown.")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/{id:guid}/payments", RetryPaymentAsync)
            .WithName("RetryPayment")
            .WithSummary("Pay again for an existing order")
            .WithDescription(
                "Starts a new payment attempt, e.g. with another card. " +
                "Only allowed when the order is `PaymentFailed` or `PaymentUnavailable`; otherwise 409.")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces<OrderDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        [FromHeader(Name = "Idempotency-Key"), Description(IdempotencyKeyDescription)] string? idempotencyKey,
        CreateOrderRequest request,
        IValidator<CreateOrderRequest> validator,
        IdempotencyExecutor idempotency,
        OrderService service,
        CancellationToken cancellationToken)
    {
        if (await Problems.ValidateAsync(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        return await idempotency.ExecuteAsync(
            idempotencyKey,
            RequestHasher.Hash(request),
            record => service.CreateAsync(request, record, cancellationToken),
            service.DescribeAsync,
            cancellationToken);
    }

    private static async Task<IResult> GetOrderAsync(Guid id, OrderService service, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken) is { } order
            ? TypedResults.Ok(order)
            : Problems.NotFound("order", id);

    private static async Task<IResult> RetryPaymentAsync(
        Guid id,
        [FromHeader(Name = "Idempotency-Key"), Description(IdempotencyKeyDescription)] string? idempotencyKey,
        RetryPaymentRequest request,
        IValidator<RetryPaymentRequest> validator,
        IdempotencyExecutor idempotency,
        OrderService service,
        CancellationToken cancellationToken)
    {
        if (await Problems.ValidateAsync(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (!await service.ExistsAsync(id, cancellationToken))
        {
            return Problems.NotFound("order", id);
        }

        return await idempotency.ExecuteAsync(
            idempotencyKey,
            RequestHasher.Hash(id, request),
            record => service.PayAgainAsync(id, request, record, cancellationToken),
            service.DescribeAsync,
            cancellationToken);
    }
}

public sealed record OrderLineRequest(
    [property: Description("A product from GET /api/products."), DefaultValue(typeof(Guid), "5eed0000-0000-4000-8000-000000000007")] Guid ProductId,
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

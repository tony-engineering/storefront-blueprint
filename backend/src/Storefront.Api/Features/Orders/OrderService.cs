using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Storefront.Api.Idempotency;
using Storefront.Api.Payments;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Orders;

/// <summary>
/// Creating, paying and reading orders (ARCHITECTURE.md §7). The order is saved <b>before</b> the provider is
/// called, so a crash mid-call leaves an order and a payment attempt the reconciler can resolve.
/// </summary>
public sealed class OrderService(
    StorefrontDbContext db,
    PaymentProcessor payments,
    TimeProvider clock,
    IOptions<JsonOptions> json)
{
    private JsonSerializerOptions JsonOptions => json.Value.SerializerOptions;

    public async Task<OrderDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? null : OrderDto.From(order);
    }

    /// <summary>Orders that are neither Pending nor Paid, most recently updated first.</summary>
    public async Task<IReadOnlyList<UnpaidOrderDto>> ListUnpaidAsync(CancellationToken cancellationToken) =>
        await db.Orders
            .AsNoTracking()
            .Where(o => o.Status != OrderStatus.Pending && o.Status != OrderStatus.Paid)
            .OrderByDescending(o => o.UpdatedAt)
            .Take(200)
            .Select(o => new UnpaidOrderDto(
                o.Id,
                o.Status,
                o.FailureReason,
                new MoneyDto(o.Total.AmountMinor, o.Total.Currency),
                o.Payments.Count,
                o.CreatedAt,
                o.UpdatedAt))
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().AnyAsync(o => o.Id == id, cancellationToken);

    /// <summary>The response for an order as it is now. Used when the request that created it never finished.</summary>
    public async Task<StoredResponse> DescribeAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .AsSplitQuery()
            .SingleAsync(o => o.Id == orderId, cancellationToken);

        return Respond(order);
    }

    /// <summary>Prices the basket on the server, saves the order, then makes one payment attempt.</summary>
    public async Task<StoredResponse> CreateAsync(
        CreateOrderRequest request, IdempotencyRecord record, CancellationToken cancellationToken, Guid? labRunId = null)
    {
        var now = clock.GetUtcNow();

        var requested = request.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => requested.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var unavailable = requested.Where(id => !products.TryGetValue(id, out var p) || p.IsDeleted).ToList();
        if (unavailable.Count > 0)
        {
            var problem = Problems.Create(
                StatusCodes.Status422UnprocessableEntity,
                "product-unavailable",
                "Some products cannot be ordered.",
                "These products do not exist or are no longer sold. Remove them and try again.");
            problem.Extensions["productIds"] = unavailable;
            return StoredResponse.FromProblem(problem, JsonOptions);
        }

        // Prices come from the catalog, never from the client.
        Order order;
        try
        {
            var lines = request.Lines
                .Select(l => new OrderLine(l.ProductId, products[l.ProductId].Name, products[l.ProductId].Price, l.Quantity))
                .ToList();
            order = Order.Create(lines, now, labRunId: labRunId);
        }
        catch (OverflowException)
        {
            return StoredResponse.FromProblem(
                Problems.Create(
                    StatusCodes.Status422UnprocessableEntity,
                    "order-too-large",
                    "The order total is too large.",
                    "Reduce the quantities and try again."),
                JsonOptions);
        }

        var attempt = order.AddPaymentAttempt(request.PaymentToken, now);

        // One transaction: the order, its first attempt and the idempotency record's link to the order.
        record.OrderId = order.Id;
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return await ChargeAndRespondAsync(order, attempt, cancellationToken);
    }

    /// <summary>Starts a new payment attempt (e.g. another card) on an order whose payment failed.</summary>
    public async Task<StoredResponse> PayAgainAsync(
        Guid orderId, RetryPaymentRequest request, IdempotencyRecord record, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return StoredResponse.FromProblem(
                Problems.Create(StatusCodes.Status404NotFound, "not-found", "Not found", $"No order with ID {orderId} exists."),
                JsonOptions);
        }

        // Only a payment that definitely did not go through can be paid again. After a timeout the first charge
        // may have succeeded, and paying again could charge the customer twice.
        if (order.Status is not (OrderStatus.PaymentFailed or OrderStatus.PaymentUnavailable))
        {
            return NotPayable(order);
        }

        var now = clock.GetUtcNow();
        order.StartNewPayment(now);
        var attempt = order.AddPaymentAttempt(request.PaymentToken, now);
        db.PaymentAttempts.Add(attempt);
        record.OrderId = order.Id;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsUniqueViolation(ex))
        {
            // Another request changed this order first (two clicks with different keys). Only one may win.
            db.ChangeTracker.Clear();
            var current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, cancellationToken);
            return NotPayable(current);
        }

        return await ChargeAndRespondAsync(order, attempt, cancellationToken);
    }

    private async Task<StoredResponse> ChargeAndRespondAsync(Order order, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        // One call to the provider, never waiting through retries (those are the reconciler's job).
        await payments.ChargeAsync(order, attempt, cancellationToken);

        // The provider has answered; record that even if the client has gone away.
        await db.SaveChangesAsync(CancellationToken.None);

        return Respond(order);
    }

    private StoredResponse NotPayable(Order order) =>
        StoredResponse.FromProblem(
            Problems.Create(
                StatusCodes.Status409Conflict,
                "order-not-payable",
                "This order cannot be paid right now.",
                $"The order is {order.Status}. Only an order whose payment failed or whose provider was unavailable can be paid again."),
            JsonOptions);

    /// <summary>201 once the outcome is settled, 202 while it is still being resolved in the background.</summary>
    private StoredResponse Respond(Order order)
    {
        var status = order.Status is OrderStatus.Pending or OrderStatus.PaymentRetrying or OrderStatus.PaymentUnknown
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status201Created;

        return StoredResponse.Json(status, OrderDto.From(order), JsonOptions, location: $"/api/orders/{order.Id}");
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}

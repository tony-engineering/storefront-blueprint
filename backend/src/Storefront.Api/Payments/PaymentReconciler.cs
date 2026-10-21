using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storefront.Api.Features.Orders;
using Storefront.Api.Persistence;

namespace Storefront.Api.Payments;

/// <summary>
/// Finishes payments that checkout could not (ARCHITECTURE.md §7). One pass handles every attempt that is due:
/// <list type="bullet">
///   <item><b>Retrying</b>: charge again with the same idempotency key, with backoff, up to the retry limit.</item>
///   <item><b>Unknown</b>: only ask the provider what happened. It never charges again.</item>
///   <item><b>Pending for too long</b>: the process died mid-call. Treated as unknown.</item>
/// </list>
/// The orders table is the work queue; there is no separate broker.
/// </summary>
public sealed class PaymentReconciler(
    StorefrontDbContext db,
    PaymentProcessor processor,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentReconciler> logger) : IPaymentReconciler
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Reconciler;
        var now = clock.GetUtcNow();
        var abandonedBefore = now - settings.PendingGracePeriod;

        var due = await db.PaymentAttempts
            .AsNoTracking()
            .Where(a =>
                (a.NextCallAt != null && a.NextCallAt <= now
                    && (a.Status == PaymentAttemptStatus.Retrying || a.Status == PaymentAttemptStatus.Unknown))
                || (a.Status == PaymentAttemptStatus.Pending && a.CreatedAt <= abandonedBefore))
            .OrderBy(a => a.CreatedAt)
            .Take(settings.BatchSize)
            .Select(a => new { a.Id, a.OrderId })
            .ToListAsync(cancellationToken);

        foreach (var item in due)
        {
            try
            {
                await ProcessAsync(item.OrderId, item.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad payment must not stop the others. It stays scheduled and is tried again next pass.
                logger.LogError(ex, "Reconciling attempt {AttemptId} of order {OrderId} failed.", item.Id, item.OrderId);
            }
        }
    }

    private async Task ProcessAsync(Guid orderId, Guid attemptId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var order = await db.Orders.Include(o => o.Payments).SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        var attempt = order?.Payments.SingleOrDefault(a => a.Id == attemptId);
        if (order is null || attempt is null)
        {
            return;
        }

        // The state may have moved on since the query (a customer paid again, another pass got there first).
        var now = clock.GetUtcNow();
        var settings = options.Value.Reconciler;
        var due = attempt.NextCallAt is { } at && at <= now;

        if (attempt is { Status: PaymentAttemptStatus.Retrying } && order.Status == OrderStatus.PaymentRetrying && due)
        {
            await processor.ChargeAsync(order, attempt, cancellationToken);
        }
        else if (attempt is { Status: PaymentAttemptStatus.Unknown } && order.Status == OrderStatus.PaymentUnknown && due)
        {
            await processor.CheckStatusAsync(order, attempt, cancellationToken);
        }
        else if (attempt is { Status: PaymentAttemptStatus.Pending } && order.Status == OrderStatus.Pending
                 && attempt.CreatedAt <= now - settings.PendingGracePeriod)
        {
            logger.LogWarning("Order {OrderId}: attempt {AttemptId} was abandoned mid-call; looking it up.", orderId, attemptId);
            order.MarkUnknown(now);
            attempt.ScheduleStatusCheck(now, now);
            await processor.CheckStatusAsync(order, attempt, cancellationToken);
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed the order while the provider was being called. Nothing is lost: retries repeat the same
            // idempotent call and status checks only read, so the next pass reaches the same answer.
            logger.LogInformation("Order {OrderId} changed during reconciliation; will look again.", orderId);
            db.ChangeTracker.Clear();
        }
    }
}

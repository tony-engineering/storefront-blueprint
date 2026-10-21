using Microsoft.EntityFrameworkCore;
using Npgsql;
using Storefront.Api.Features;
using Storefront.Api.Persistence;

namespace Storefront.Api.Idempotency;

/// <summary>
/// Makes a <c>POST</c> safe to repeat (ARCHITECTURE.md §6). The first request with a key inserts an
/// <see cref="IdempotencyState.InProgress"/> record; a repeat finds it through the primary-key constraint, so the
/// database, not a lock in this process, decides who is first. That keeps working with several API instances.
/// <list type="table">
///   <item><term>Key missing or not a UUID</term><description>400</description></item>
///   <item><term>New key</term><description>run the request, store its final response</description></item>
///   <item><term>Same key, same body, finished</term><description>the stored response again, plus <c>Idempotent-Replayed: true</c></description></item>
///   <item><term>Same key, different body</term><description>422</description></item>
///   <item><term>Same key, first request still running</term><description>409 with <c>Retry-After: 1</c></description></item>
/// </list>
/// A request that has been "running" for over <see cref="StaleAfter"/> lost its process. If it had already created
/// the order, the repeat gets that order's current state; if not, the repeat takes the key over.
/// </summary>
public sealed class IdempotencyExecutor(
    StorefrontDbContext db,
    TimeProvider clock,
    ILogger<IdempotencyExecutor> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    private const int MaxClaimAttempts = 3;

    /// <param name="keyHeader">The raw <c>Idempotency-Key</c> header.</param>
    /// <param name="requestHash">Fingerprint of the body (<see cref="RequestHasher"/>).</param>
    /// <param name="action">Does the work for a new key. It may set <see cref="IdempotencyRecord.OrderId"/> before saving.</param>
    /// <param name="describeOrder">Builds the response for an order whose request died half way.</param>
    public async Task<IResult> ExecuteAsync(
        string? keyHeader,
        string requestHash,
        Func<IdempotencyRecord, Task<StoredResponse>> action,
        Func<Guid, CancellationToken, Task<StoredResponse>> describeOrder,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(keyHeader, out var key) || key == Guid.Empty)
        {
            return Problems.Result(
                StatusCodes.Status400BadRequest,
                "idempotency-key-invalid",
                "Idempotency-Key header is required.",
                "Send a unique UUID in the Idempotency-Key header and reuse it only to retry the same request.");
        }

        for (var round = 0; round < MaxClaimAttempts; round++)
        {
            var record = IdempotencyRecord.Begin(key, requestHash, clock.GetUtcNow());
            db.IdempotencyRecords.Add(record);

            var claimed = true;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                claimed = false;
                db.ChangeTracker.Clear();
            }

            if (claimed)
            {
                return await RunAsync(record, action);
            }

            var (result, owned) = await HandleExistingAsync(key, requestHash, describeOrder, cancellationToken);
            if (result is not null)
            {
                return result;
            }

            if (owned is not null)
            {
                return await RunAsync(owned, action);
            }

            // The record disappeared between the insert and the read (its owner failed and removed it): try again.
        }

        return Conflict();
    }

    private async Task<IResult> RunAsync(IdempotencyRecord record, Func<IdempotencyRecord, Task<StoredResponse>> action)
    {
        StoredResponse response;
        try
        {
            response = await action(record);
        }
        catch
        {
            if (record.OrderId is null)
            {
                // Nothing durable happened, so free the key and let the client retry straight away.
                await db.IdempotencyRecords.Where(r => r.Key == record.Key).ExecuteDeleteAsync(CancellationToken.None);
            }

            throw;
        }

        var now = clock.GetUtcNow();
        var orderId = record.OrderId;
        await db.IdempotencyRecords
            .Where(r => r.Key == record.Key)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.State, IdempotencyState.Completed)
                    .SetProperty(r => r.OrderId, orderId)
                    .SetProperty(r => r.ResponseStatus, response.Status)
                    .SetProperty(r => r.ResponseBody, response.Body)
                    .SetProperty(r => r.Location, response.Location)
                    .SetProperty(r => r.CompletedAt, now),
                CancellationToken.None);

        return new StoredResponseResult(response, replayed: false);
    }

    private async Task<(IResult? Result, IdempotencyRecord? Owned)> HandleExistingAsync(
        Guid key,
        string requestHash,
        Func<Guid, CancellationToken, Task<StoredResponse>> describeOrder,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key, cancellationToken);
        if (existing is null)
        {
            return (null, null);
        }

        if (existing.RequestHash != requestHash)
        {
            return (Problems.Result(
                StatusCodes.Status422UnprocessableEntity,
                "idempotency-key-reused",
                "Idempotency-Key was already used for a different request.",
                "Use a new Idempotency-Key for a different request; reuse a key only to retry the same one."), null);
        }

        if (existing.State == IdempotencyState.Completed)
        {
            var stored = new StoredResponse(existing.ResponseStatus!.Value, existing.ResponseBody!, existing.Location);
            return (new StoredResponseResult(stored, replayed: true), null);
        }

        var now = clock.GetUtcNow();
        if (now - existing.CreatedAt < StaleAfter)
        {
            return (Conflict(), null);
        }

        // The first request has been "running" for too long: its process is gone.
        if (existing.OrderId is { } orderId)
        {
            logger.LogWarning("Idempotency key {Key} never completed; answering with the current state of order {OrderId}.", key, orderId);
            return (new StoredResponseResult(await describeOrder(orderId, cancellationToken), replayed: true), null);
        }

        // It never created an order, so nothing happened. Take the key over; only one competing request can win.
        var taken = await db.IdempotencyRecords
            .Where(r => r.Key == key && r.State == IdempotencyState.InProgress && r.CreatedAt == existing.CreatedAt)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.CreatedAt, now), cancellationToken);
        if (taken == 0)
        {
            return (null, null);
        }

        logger.LogWarning("Idempotency key {Key} was abandoned before an order existed; taking it over.", key);
        var owned = await db.IdempotencyRecords.SingleAsync(r => r.Key == key, cancellationToken);
        return (null, owned);
    }

    private static IResult Conflict() =>
        new HeaderResult(
            Problems.Result(
                StatusCodes.Status409Conflict,
                "request-in-progress",
                "The first request with this Idempotency-Key is still being processed.",
                "Wait a moment and send the same request again to get its result."),
            "Retry-After",
            "1");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

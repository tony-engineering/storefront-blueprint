using Microsoft.EntityFrameworkCore;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Lab;

/// <summary>Support for the load-testing lab: order counts by status for one run (the load generator polls it).</summary>
public static class LabEndpoints
{
    public static IServiceCollection AddLab(this IServiceCollection services)
    {
        services.AddOptions<LabOptions>().BindConfiguration(LabOptions.SectionName);
        services.AddSingleton<LabOrderJanitor>();
        services.AddHostedService(provider => provider.GetRequiredService<LabOrderJanitor>());
        return services;
    }

    public static IEndpointRouteBuilder MapLabEndpoints(this IEndpointRouteBuilder app)
    {
        // Deliberately not under /api/lab: that prefix is routed to the load generator.
        app.MapGet("/api/admin/lab/runs/{runId:guid}/order-stats", GetOrderStatsAsync)
            .WithTags("Admin: lab")
            .WithName("GetLabOrderStats")
            .WithSummary("Order counts by status for one load-test run")
            .WithDescription("Counts the orders sent with `X-Lab-Run: {runId}`.")
            .Produces<LabOrderStats>();

        // Read-only: the load generator calls this to reconcile keys it never got an answer for, deciding
        // whether a silent request still created an order. It must never create one, so only GET.
        app.MapGet("/api/admin/lab/orders/by-key/{key:guid}", GetOrderByKeyAsync)
            .WithTags("Admin: lab")
            .WithName("GetLabOrderByKey")
            .WithSummary("The order an idempotency key created, if any")
            .WithDescription("Looks up the order recorded against one `Idempotency-Key`. 404 when the key created no order.")
            .Produces<LabKeyOrder>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetOrderStatsAsync(Guid runId, StorefrontDbContext db, CancellationToken cancellationToken)
    {
        var groups = await db.Orders
            .AsNoTracking()
            .Where(o => o.LabRunId == runId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = groups.ToDictionary(g => g.Status.ToString(), g => g.Count);
        return TypedResults.Ok(new LabOrderStats(byStatus.Values.Sum(), byStatus));
    }

    private static async Task<IResult> GetOrderByKeyAsync(Guid key, StorefrontDbContext db, CancellationToken cancellationToken)
    {
        // The idempotency record gets its OrderId in the same transaction as the order, so if a request ever
        // created an order the record points at it, even when the caller never received the reply.
        var orderId = await db.IdempotencyRecords
            .AsNoTracking()
            .Where(r => r.Key == key && r.OrderId != null)
            .Select(r => r.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (orderId is not { } id)
        {
            return TypedResults.NotFound();
        }

        var status = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => o.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new LabKeyOrder(id, status.ToString()));
    }
}

public sealed record LabOrderStats(int Total, IReadOnlyDictionary<string, int> ByStatus);

public sealed record LabKeyOrder(Guid OrderId, string Status);

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
}

public sealed record LabOrderStats(int Total, IReadOnlyDictionary<string, int> ByStatus);

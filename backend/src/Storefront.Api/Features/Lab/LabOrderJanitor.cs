using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Lab;

/// <summary>
/// Deletes lab orders older than <see cref="LabOptions.RetainFor"/>, with their idempotency records.
/// Lines and payment attempts follow through the cascade. Real orders (no lab run) are never touched.
/// </summary>
public sealed class LabOrderJanitor(
    IServiceScopeFactory scopes,
    IOptions<LabOptions> options,
    TimeProvider clock,
    ILogger<LabOrderJanitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.JanitorInterval;
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Purging old lab orders failed; trying again next time.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes lab orders created before now − <see cref="LabOptions.RetainFor"/>; returns how many.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StorefrontDbContext>();
        var cutoff = clock.GetUtcNow() - options.Value.RetainFor;

        var old = db.Orders.Where(o => o.LabRunId != null && o.CreatedAt < cutoff);
        await db.IdempotencyRecords
            .Where(r => r.OrderId != null && old.Select(o => o.Id).Contains(r.OrderId.Value))
            .ExecuteDeleteAsync(cancellationToken);
        var deleted = await old.ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} lab orders older than {Cutoff:u}.", deleted, cutoff);
        }

        return deleted;
    }
}

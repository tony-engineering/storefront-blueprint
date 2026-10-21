using Microsoft.Extensions.Options;

namespace Storefront.Api.Payments;

/// <summary>
/// Runs <see cref="IPaymentReconciler"/> every few seconds. The pass itself lives in
/// <see cref="IPaymentReconciler.RunOnceAsync"/>, so tests can drive it step by step against a fake clock
/// with this loop switched off (<c>Payments:Reconciler:Enabled=false</c>).
/// </summary>
public sealed class PaymentReconcilerService(
    IServiceScopeFactory scopes,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentReconcilerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Reconciler;
        if (!settings.Enabled)
        {
            logger.LogInformation("The payment reconciler loop is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(settings.Interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "A payment reconciliation pass failed; trying again on the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}

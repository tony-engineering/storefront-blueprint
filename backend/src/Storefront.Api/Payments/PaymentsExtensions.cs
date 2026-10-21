using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Storefront.Api.Payments;

public static class PaymentsExtensions
{
    /// <summary>
    /// Registers the payment flow. The gateway is the mock; swapping in a real provider means registering
    /// another <see cref="IPaymentGateway"/> here. Settings are bound lazily, so values supplied later
    /// (environment variables, the integration tests) still apply.
    /// </summary>
    public static IServiceCollection AddPayments(this IServiceCollection services)
    {
        services.AddOptions<PaymentOptions>().BindConfiguration(PaymentOptions.SectionName);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPaymentGateway, MockPaymentGateway>();
        services.AddSingleton<PaymentProcessor>();
        services.AddScoped<IPaymentReconciler, PaymentReconciler>();

        // Always registered; the loop checks Payments:Reconciler:Enabled when it starts.
        services.AddHostedService<PaymentReconcilerService>();
        return services;
    }
}

using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Storefront.Api.Payments;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API in-process against the test database. Two things are replaced:
/// <list type="bullet">
///   <item>the clock, with a <see cref="FakeTimeProvider"/>, so "the provider is down for 30 seconds"
///   is simulated time and the tests never sleep;</item>
///   <item>the payment provider, with a <see cref="ScriptedPaymentGateway"/> whose outages and
///   declines each test scripts.</item>
/// </list>
/// The reconciler's background loop is switched off; tests drive it with
/// <see cref="RunReconcilerAsync"/> so every retry happens at a known moment.
/// </summary>
public sealed class StorefrontApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Retry policy used by every test. Defined here so assertions can refer to it.</summary>
    public static class RetryPolicy
    {
        /// <summary>Total calls to the provider for one payment attempt, the first (inline) call included.</summary>
        public const int MaxChargeAttempts = 6;

        public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
        public const double BackoffMultiplier = 2;
        public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

        // Calls happen at t = 0, 2, 6, 14, 30, 62 s: a 30-second outage is ridden out,
        // and the customer hears back about a long outage after about one minute.
    }

    private static readonly DateTimeOffset StartTime = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;

    public StorefrontApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        Clock = new FakeTimeProvider(StartTime);
        Gateway = new ScriptedPaymentGateway(Clock);
    }

    public FakeTimeProvider Clock { get; }

    public ScriptedPaymentGateway Gateway { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Storefront", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedOnStartup", "true");

        builder.UseSetting("Auth:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:DevTokenEndpoint", "true");

        builder.UseSetting("Payments:Reconciler:Enabled", "false");
        builder.UseSetting("Payments:Retry:MaxChargeAttempts",
            RetryPolicy.MaxChargeAttempts.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Payments:Retry:InitialDelay",
            RetryPolicy.InitialDelay.ToString("c", CultureInfo.InvariantCulture));
        builder.UseSetting("Payments:Retry:BackoffMultiplier",
            RetryPolicy.BackoffMultiplier.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Payments:Retry:MaxDelay",
            RetryPolicy.MaxDelay.ToString("c", CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);
        });
    }

    /// <summary>Runs one reconciler pass: every payment whose next retry is due is processed.</summary>
    public async Task RunReconcilerAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IPaymentReconciler>();
        await reconciler.RunOnceAsync(cancellationToken);
    }
}

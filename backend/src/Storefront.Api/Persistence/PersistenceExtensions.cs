using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Storefront.Api.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Storefront";

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);

        // The connection string is read when the first DbContext is created, not at registration,
        // so settings added later (environment variables, WebApplicationFactory.UseSetting) still apply.
        services.AddDbContext<StorefrontDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings:{ConnectionStringName} " +
                    $"(environment variable ConnectionStrings__{ConnectionStringName}).");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbSeeder>();
        return services;
    }

    /// <summary>
    /// Applies migrations and seeds the demo catalog, as switched on by <see cref="DatabaseOptions"/>.
    /// Runs before the API accepts requests, so the first request never sees a missing table or an empty store.
    /// Any failure stops the start-up: an API without its schema would only fail later, on every request.
    /// </summary>
    public static Task InitialiseDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        return app.ApplyDatabaseAsync(options.MigrateOnStartup, options.SeedOnStartup, cancellationToken);
    }

    /// <summary>
    /// The deploy pipeline's migration step: always applies migrations, and seeds only if
    /// <see cref="DatabaseOptions.SeedOnStartup"/> is on. Runs once per deploy, before the new API pods start.
    /// </summary>
    public static Task MigrateDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        return app.ApplyDatabaseAsync(migrate: true, options.SeedOnStartup, cancellationToken);
    }

    private static async Task ApplyDatabaseAsync(this WebApplication app, bool migrate, bool seed, CancellationToken cancellationToken)
    {
        if (!migrate && !seed)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();

        if (migrate)
        {
            var db = scope.ServiceProvider.GetRequiredService<StorefrontDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (seed)
        {
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(cancellationToken);
        }
    }
}

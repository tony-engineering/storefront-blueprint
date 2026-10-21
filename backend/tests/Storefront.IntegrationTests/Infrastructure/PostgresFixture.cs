using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Xunit;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL container per test run, configured exactly like the <c>postgres</c>
/// service in docker-compose.yml: same image, data directory on tmpfs, durability off.
/// Each test starts from an empty database (Respawn); the API re-seeds the catalog on start-up.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("storefront")
        .WithUsername("storefront")
        .WithPassword("storefront")
        // From PostgreSQL 18 the data directory lives below /var/lib/postgresql/<major>/,
        // so the tmpfs must be mounted at the parent to keep the data in RAM.
        .WithTmpfsMount("/var/lib/postgresql")
        .WithCommand(
            "-c", "fsync=off",
            "-c", "synchronous_commit=off",
            "-c", "full_page_writes=off")
        .Build();

    private Respawner? _respawner;

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Boot the API once so it applies the EF Core migrations.
        // Respawn needs the tables to exist before it can build its delete plan.
        await using (var api = new StorefrontApiFactory(ConnectionString))
        {
            _ = api.Services;
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")],
        });
    }

    /// <summary>Deletes every row except the migration history.</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null)
        {
            throw new InvalidOperationException("The fixture has not been initialised.");
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

namespace Storefront.Api.Persistence;

/// <summary>
/// The <c>Database</c> configuration section. Both switches are off by default (appsettings.json)
/// and on in Development and in the integration tests. In the Kubernetes deployment, migrations run as
/// a separate step (<c>Storefront.Api migrate</c>, a Helm pre-upgrade Job) and API pods start with both off.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations before the API starts serving requests.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Insert the demo catalog (<see cref="DbSeeder"/>) if the database has no products yet.</summary>
    public bool SeedOnStartup { get; init; }
}

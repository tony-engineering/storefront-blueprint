namespace Storefront.Api.Persistence;

/// <summary>
/// The <c>Database</c> configuration section. Both switches are off by default (appsettings.json)
/// and on in Development and in the integration tests. In a multi-instance production deployment,
/// migrations would run as a separate pipeline step and there would be no demo data.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations before the API starts serving requests.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Insert the demo catalog (<see cref="DbSeeder"/>) if the database has no products yet.</summary>
    public bool SeedOnStartup { get; init; }
}

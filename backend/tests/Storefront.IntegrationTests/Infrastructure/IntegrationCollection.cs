using Xunit;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>
/// All integration tests share one database, so they run in a single collection.
/// xUnit runs the tests of one collection sequentially; collections run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}

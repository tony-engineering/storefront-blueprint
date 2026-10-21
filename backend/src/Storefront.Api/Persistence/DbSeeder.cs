using Microsoft.EntityFrameworkCore;
using Npgsql;
using Storefront.Api.Common;
using Storefront.Api.Features.Orders;
using Storefront.Api.Features.Products;

namespace Storefront.Api.Persistence;

/// <summary>
/// Inserts the demo catalog (and one paid demo order) when the database has no products yet (ARCHITECTURE.md §8).
/// <list type="bullet">
///   <item><b>Idempotent:</b> any existing product, live or soft-deleted, means the store has been
///   seeded or is in use, so nothing is touched. Restarting the API never adds duplicates.</item>
///   <item><b>All or nothing:</b> the catalog is saved in one <c>SaveChanges</c>, i.e. one transaction.</item>
///   <item><b>Safe when two instances start together:</b> IDs are fixed, so the slower instance hits
///   a primary-key violation, rolls back and treats the catalog as already seeded.</item>
/// </list>
/// </summary>
public sealed class DbSeeder(StorefrontDbContext db, ILogger<DbSeeder> logger)
{
    /// <summary>Fixed, so demos, Swagger examples and tests can refer to products by ID.</summary>
    public static class ProductIds
    {
        public static readonly Guid Wren = new("5eed0000-0000-4000-8000-000000000001");
        public static readonly Guid Hollow = new("5eed0000-0000-4000-8000-000000000002");
        public static readonly Guid Harbour = new("5eed0000-0000-4000-8000-000000000003");
        public static readonly Guid Meadow = new("5eed0000-0000-4000-8000-000000000004");
        public static readonly Guid Shepherd = new("5eed0000-0000-4000-8000-000000000005");
        public static readonly Guid Lantern = new("5eed0000-0000-4000-8000-000000000006");
        public static readonly Guid Burrow = new("5eed0000-0000-4000-8000-000000000007");

        /// <summary>Seeded already soft-deleted: in the database, but not in the catalog.</summary>
        public static readonly Guid Prototype = new("5eed0000-0000-4000-8000-0000000000ff");
    }

    /// <summary>Fixed, so Swagger and demos can fetch a real order without creating one first.</summary>
    public static readonly Guid DemoOrderId = new("0de20000-0000-4000-8000-000000000001");

    // Fixed rather than "now", so the seed data is identical on every run.
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The demo catalog. Built through <see cref="Product.Create"/>, so it obeys the same rules as admin-created products.</summary>
    public static IReadOnlyList<Product> CreateCatalog()
    {
        Product[] catalog =
        [
            Product.Create(ProductIds.Wren, "The Wren",
                "An 18 m² cabin on a road-legal trailer. Sleeps two, with a compact kitchen and a wet room.",
                Money.FromPence(3_495_000), SeededAt),
            Product.Create(ProductIds.Hollow, "The Hollow",
                "Timber A-frame with a loft bedroom under the roofline and a wood-burning stove.",
                Money.FromPence(4_250_000), SeededAt),
            Product.Create(ProductIds.Harbour, "The Harbour",
                "A 40 ft shipping container converted into a 28 m² home, insulated for all seasons.",
                Money.FromPence(3_875_000), SeededAt),
            Product.Create(ProductIds.Meadow, "The Meadow",
                "Fully off-grid: rooftop solar, battery storage, rainwater harvesting and a composting toilet.",
                Money.FromPence(5_690_000), SeededAt),
            Product.Create(ProductIds.Shepherd, "The Shepherd",
                "A classic shepherd's hut on cast-iron wheels, with a double bed and a stove.",
                Money.FromPence(2_499_500), SeededAt),
            Product.Create(ProductIds.Lantern, "The Lantern",
                "A glass-fronted garden studio. Works as a home office or a guest room.",
                Money.FromPence(1_949_999), SeededAt),
            Product.Create(ProductIds.Burrow, "The Burrow",
                "Earth-sheltered with a turf roof. Stays cool in summer and warm in winter.",
                Money.FromPence(6_120_000), SeededAt),
            Product.Create(ProductIds.Prototype, "The Prototype",
                "Discontinued. Seeded as deleted to show that deleted products are hidden from the catalog.",
                Money.FromPence(999_900), SeededAt),
        ];

        catalog.Single(p => p.Id == ProductIds.Prototype).Delete();
        return catalog;
    }

    /// <summary>One <c>Paid</c> order for a single "The Burrow", built through the same domain methods a real checkout uses.</summary>
    public static Order CreateDemoOrder(IReadOnlyList<Product> catalog)
    {
        var burrow = catalog.Single(p => p.Id == ProductIds.Burrow);
        var order = Order.Create([new OrderLine(burrow.Id, burrow.Name, burrow.Price, 1)], SeededAt, DemoOrderId);

        var attempt = order.AddPaymentAttempt("tok_success", SeededAt);
        attempt.RecordChargeCall();
        attempt.MarkSucceeded("ch_seed_0001", SeededAt);
        order.MarkPaid(SeededAt);
        return order;
    }

    public async Task<SeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        // Counts soft-deleted products too: a catalog whose products were all deleted is not "empty".
        var seedCatalog = !await db.Products.AnyAsync(cancellationToken);
        // Checked on its own, so a database seeded before the demo order existed still gets it. Orders are never deleted.
        var seedOrder = !await db.Orders.AnyAsync(o => o.Id == DemoOrderId, cancellationToken);

        if (!seedCatalog && !seedOrder)
        {
            logger.LogInformation("Seed skipped: the catalog and the demo order already exist.");
            return SeedResult.AlreadySeeded;
        }

        // The demo order is built from the fixed catalog, so it does not depend on what is in the database now.
        var catalog = CreateCatalog();
        if (seedCatalog)
        {
            db.Products.AddRange(catalog);
        }

        if (seedOrder)
        {
            db.Orders.Add(CreateDemoOrder(catalog));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance seeded between our check and our insert. Its transaction won; ours rolled back.
            db.ChangeTracker.Clear();
            logger.LogInformation("Seed skipped: another instance seeded at the same time.");
            return SeedResult.AlreadySeeded;
        }

        logger.LogInformation(
            "Seeded: catalog {Catalog} ({Total} products, {Deleted} soft-deleted), demo order {Order}.",
            seedCatalog, catalog.Count, catalog.Count(p => p.IsDeleted), seedOrder);
        return SeedResult.Seeded;
    }
}

public enum SeedResult
{
    Seeded,
    AlreadySeeded,
}

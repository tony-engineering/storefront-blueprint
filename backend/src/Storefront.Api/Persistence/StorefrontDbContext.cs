using Microsoft.EntityFrameworkCore;
using Storefront.Api.Features.Orders;
using Storefront.Api.Features.Products;
using Storefront.Api.Idempotency;

namespace Storefront.Api.Persistence;

public sealed class StorefrontDbContext(DbContextOptions<StorefrontDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StorefrontDbContext).Assembly);
}

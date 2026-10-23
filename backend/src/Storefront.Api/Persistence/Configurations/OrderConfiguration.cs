using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Storefront.Api.Features.Orders;

namespace Storefront.Api.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_TotalAmountMinor_Positive", "\"TotalAmountMinor\" > 0");
            table.HasCheckConstraint("CK_Orders_TotalCurrency_Iso4217", "\"TotalCurrency\" ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.FailureReason).HasMaxLength(100);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.UpdatedAt).IsRequired();

        // PostgreSQL's xmin system column: changes with every update, so a second writer that loaded the
        // order before the first saved gets a DbUpdateConcurrencyException instead of overwriting it.
        builder.Property(o => o.Version).IsRowVersion();

        builder.ComplexProperty(o => o.Total, total =>
        {
            total.Property(m => m.AmountMinor).HasColumnName("TotalAmountMinor").IsRequired();
            total.Property(m => m.Currency).HasColumnName("TotalCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Payments)
            .WithOne()
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => o.Status);

        // Lab stats group by run; the janitor deletes by run and age.
        builder.HasIndex(o => o.LabRunId).HasFilter("\"LabRunId\" IS NOT NULL");
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines", table =>
        {
            table.HasCheckConstraint("CK_OrderLines_Quantity_Positive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_OrderLines_UnitPriceCurrency_Iso4217", "\"UnitPriceCurrency\" ~ '^[A-Z]{3}$'");
        });

        builder.Property<Guid>("OrderId");
        builder.HasKey("OrderId", nameof(OrderLine.Position));

        builder.Property(l => l.Position).ValueGeneratedNever();
        builder.Property(l => l.ProductName).HasMaxLength(Features.Products.Product.NameMaxLength).IsRequired();

        builder.ComplexProperty(l => l.UnitPrice, price =>
        {
            price.Property(m => m.AmountMinor).HasColumnName("UnitPriceAmountMinor").IsRequired();
            price.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        // A snapshot, deliberately without a foreign key to Products: products are only ever soft-deleted,
        // and an order line must stay valid whatever happens to the catalog.
        builder.HasIndex("OrderId", nameof(OrderLine.ProductId)).IsUnique();
    }
}

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("PaymentAttempts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.PaymentToken).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.FailureReason).HasMaxLength(100);
        builder.Property(a => a.ChargeId).HasMaxLength(100);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        // Two writers starting a payment on the same order at once collide here.
        builder.HasIndex(a => new { a.OrderId, a.Number }).IsUnique();

        // The reconciler's query: attempts whose retry or status check is due.
        builder.HasIndex(a => a.NextCallAt).HasFilter("\"NextCallAt\" IS NOT NULL");
        builder.HasIndex(a => a.Status);
    }
}

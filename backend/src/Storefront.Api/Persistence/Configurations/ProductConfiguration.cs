using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Storefront.Api.Features.Products;

namespace Storefront.Api.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            // The domain already rejects these; the database is the last line of defence.
            table.HasCheckConstraint("CK_Products_PriceAmountMinor_Positive", "\"PriceAmountMinor\" > 0");
            table.HasCheckConstraint("CK_Products_PriceCurrency_Iso4217", "\"PriceCurrency\" ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(p => p.Id);
        // IDs come from the domain (fixed IDs for seed data, Guid.CreateVersion7() for new products).
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.IsDeleted).HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.AmountMinor).HasColumnName("PriceAmountMinor").IsRequired();
            price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        // The public catalog query: non-deleted products, ordered by name.
        builder.HasIndex(p => new { p.IsDeleted, p.Name });
    }
}

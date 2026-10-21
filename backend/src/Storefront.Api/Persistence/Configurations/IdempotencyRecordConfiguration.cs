using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Storefront.Api.Idempotency;

namespace Storefront.Api.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        // The primary key is the unique index that makes the first of several concurrent requests win.
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).ValueGeneratedNever();

        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.Location).HasMaxLength(200);
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}

using Storefront.Api.Common;

namespace Storefront.Api.Features.Products;

/// <summary>
/// A catalog item. Products are soft-deleted: hidden from the catalog and rejected for new orders,
/// but still resolvable from old orders (ARCHITECTURE.md §4).
/// </summary>
public sealed class Product
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private Product(Guid id, string name, string? description, Money price, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Description = description;
        Price = price;
        CreatedAt = createdAt;
    }

    // For EF Core: complex properties (Price) cannot be bound through the constructor.
    private Product()
    {
        Name = null!;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Money Price { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The single way to make a product, used by both the admin endpoint and the seeder,
    /// so seed data cannot bypass the rules the API enforces.
    /// </summary>
    public static Product Create(Guid id, string name, string? description, Money price, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Product ID must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (name.Length > NameMaxLength)
        {
            throw new ArgumentException($"Name must be at most {NameMaxLength} characters.", nameof(name));
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException($"Description must be at most {DescriptionMaxLength} characters.", nameof(description));
        }

        if (price.AmountMinor <= 0)
        {
            throw new ArgumentException("Price must be positive.", nameof(price));
        }

        return new Product(id, name, description, price, createdAt);
    }

    /// <summary>Idempotent: deleting an already deleted product changes nothing.</summary>
    public void Delete() => IsDeleted = true;
}

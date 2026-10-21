using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Products;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", ListProductsAsync)
            .WithTags("Products")
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription("Returns the catalog. Deleted products are not included.");

        var admin = app.MapGroup("/api/admin/products")
            .WithTags("Admin: products");

        admin.MapPost("", (CreateProductRequest request) =>
                NotImplementedYet.Problem("Creating a product"))
            .WithName("CreateProduct")
            .WithSummary("Create a product")
            .WithDescription("Admin only. Returns 201 with a `Location` header; 400 with per-field errors if invalid.");

        admin.MapDelete("/{id:guid}", (Guid id) =>
                NotImplementedYet.Problem("Deleting a product"))
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .WithDescription(
                "Admin only. Soft delete: the product leaves the catalog but stays on existing orders. " +
                "Idempotent: deleting an already deleted product still returns 204.");

        return app;
    }

    /// <summary>The public catalog: live products only, ordered by name.</summary>
    private static async Task<IResult> ListProductsAsync(StorefrontDbContext db, CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Description,
                new MoneyDto(p.Price.AmountMinor, p.Price.Currency)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(products);
    }
}

public sealed record CreateProductRequest(
    [property: Description("Display name. Required.")] string Name,
    [property: Description("Optional longer description.")] string? Description,
    [property: Description("Price in minor units (pence), e.g. 1999 for £19.99. Must be positive."), DefaultValue(1999L)] long PriceMinor);

public sealed record ProductDto(Guid Id, string Name, string? Description, MoneyDto Price);

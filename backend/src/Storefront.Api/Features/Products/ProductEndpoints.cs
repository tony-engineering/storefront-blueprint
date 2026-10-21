using Microsoft.EntityFrameworkCore;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Products;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", ListProductsAsync);

        var admin = app.MapGroup("/api/admin/products");

        admin.MapPost("", (CreateProductRequest request) =>
            NotImplementedYet.Problem("Creating a product"));

        admin.MapDelete("/{id:guid}", (Guid id) =>
            NotImplementedYet.Problem("Deleting a product"));

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

public sealed record CreateProductRequest(string Name, string? Description, long PriceMinor);

public sealed record ProductDto(Guid Id, string Name, string? Description, MoneyDto Price);

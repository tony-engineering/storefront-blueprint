using System.ComponentModel;

namespace Storefront.Api.Features.Products;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", () =>
                NotImplementedYet.Problem("Listing products"))
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
}

public sealed record CreateProductRequest(
    [property: Description("Display name. Required.")] string Name,
    [property: Description("Optional longer description.")] string? Description,
    [property: Description("Price in minor units (pence), e.g. 1999 for £19.99. Must be positive."), DefaultValue(1999L)] long PriceMinor);

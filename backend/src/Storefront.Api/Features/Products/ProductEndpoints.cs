namespace Storefront.Api.Features.Products;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", () =>
            NotImplementedYet.Problem("Listing products"));

        var admin = app.MapGroup("/api/admin/products");

        admin.MapPost("", (CreateProductRequest request) =>
            NotImplementedYet.Problem("Creating a product"));

        admin.MapDelete("/{id:guid}", (Guid id) =>
            NotImplementedYet.Problem("Deleting a product"));

        return app;
    }
}

public sealed record CreateProductRequest(string Name, string? Description, long PriceMinor);

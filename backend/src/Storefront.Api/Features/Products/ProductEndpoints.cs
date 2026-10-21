using System.ComponentModel;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Storefront.Api.Common;
using Storefront.Api.Persistence;

namespace Storefront.Api.Features.Products;

public static class ProductEndpoints
{
    public static IServiceCollection AddProducts(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateProductRequest>, CreateProductRequestValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", ListProductsAsync)
            .WithTags("Products")
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription("Returns the catalog. Deleted products are not included.");

        app.MapGet("/api/products/{id:guid}", GetProductAsync)
            .WithTags("Products")
            .WithName("GetProduct")
            .WithSummary("Get a product")
            .WithDescription("404 if the product does not exist or has been deleted.")
            .Produces<ProductDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/api/admin/products")
            .WithTags("Admin: products");

        admin.MapPost("", CreateProductAsync)
            .WithName("CreateProduct")
            .WithSummary("Create a product")
            .WithDescription("Returns 201 with a `Location` header; 400 with per-field errors if invalid.")
            .Produces<ProductDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        admin.MapDelete("/{id:guid}", DeleteProductAsync)
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .WithDescription(
                "Soft delete: the product leaves the catalog but stays on existing orders. " +
                "Idempotent: deleting an already deleted product still returns 204. 404 if the ID never existed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<IResult> GetProductAsync(Guid id, StorefrontDbContext db, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        return product is null ? Problems.NotFound("product", id) : TypedResults.Ok(ProductDto.From(product));
    }

    private static async Task<IResult> CreateProductAsync(
        CreateProductRequest request,
        IValidator<CreateProductRequest> validator,
        StorefrontDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await Problems.ValidateAsync(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // The same factory the seeder uses, so every product obeys the same rules.
        var product = Product.Create(
            Guid.CreateVersion7(), request.Name, request.Description, Money.FromPence(request.PriceMinor), clock.GetUtcNow());

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/products/{product.Id}", ProductDto.From(product));
    }

    private static async Task<IResult> DeleteProductAsync(Guid id, StorefrontDbContext db, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Problems.NotFound("product", id);
        }

        if (!product.IsDeleted)
        {
            product.Delete();
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }
}

public sealed record CreateProductRequest(
    [property: Description("Display name. Required.")] string Name,
    [property: Description("Optional longer description.")] string? Description,
    [property: Description("Price in minor units (pence), e.g. 1999 for £19.99. Must be positive."), DefaultValue(1999L)] long PriceMinor);

public sealed record ProductDto(Guid Id, string Name, string? Description, MoneyDto Price)
{
    public static ProductDto From(Product product) =>
        new(product.Id, product.Name, product.Description, MoneyDto.From(product.Price));
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    /// <summary>£100,000,000. Far above any real price; keeps order totals comfortably inside a <see cref="long"/>.</summary>
    public const long MaxPriceMinor = 10_000_000_000;

    public CreateProductRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(r => r.PriceMinor).GreaterThan(0).WithMessage("Price must be positive.")
            .LessThanOrEqualTo(MaxPriceMinor).WithMessage($"Price must be at most {MaxPriceMinor} (minor units).");
    }
}

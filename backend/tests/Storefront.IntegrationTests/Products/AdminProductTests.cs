using System.Net;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;

namespace Storefront.IntegrationTests.Products;

/// <summary>Only an admin may change the catalog; deleting a product must never rewrite an old order.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdminProductTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private static readonly CreateProductRequest NewHouse = new("The Test Cabin", "Built by a test.", 1_234_500);

    [Fact]
    public async Task The_catalog_is_public()
    {
        var response = await Client.GetProductsAsync(Ct);

        response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.NotEmpty(response.RequiredBody);
    }

    [Fact]
    public async Task Creating_a_product_needs_a_token()
    {
        var response = await Client.CreateProductAsync(NewHouse, Ct);

        response.ShouldHaveStatus(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Creating_a_product_is_forbidden_for_a_customer()
    {
        var customer = await SignedInAsync("customer");

        var response = await customer.CreateProductAsync(NewHouse, Ct);

        response.ShouldHaveStatus(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_creates_a_product_that_shows_up_in_the_catalog()
    {
        var admin = await SignedInAsync("admin");

        var response = await admin.CreateProductAsync(NewHouse, Ct);

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var created = response.RequiredBody;
        Assert.Equal(NewHouse.Name, created.Name);
        Assert.Equal(new MoneyDto(NewHouse.PriceMinor, "GBP"), created.Price);
        Assert.EndsWith($"/api/products/{created.Id}", response.Headers.Location?.OriginalString ?? "");

        var listed = (await Client.GetProductsAsync(Ct)).RequiredBody;
        Assert.Contains(listed, p => p.Id == created.Id);
        (await Client.GetProductAsync(created.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("", 1000, "name")]
    [InlineData("   ", 1000, "name")]
    [InlineData("Free house", 0, "priceMinor")]
    [InlineData("Debt house", -5, "priceMinor")]
    public async Task Invalid_products_are_rejected_with_a_field_error(string name, long price, string field)
    {
        var admin = await SignedInAsync("admin");

        var response = await admin.CreateProductAsync(new CreateProductRequest(name, null, price), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Contains(field, response.RequiredProblem.Errors!.Keys);
    }

    [Fact]
    public async Task Deleting_a_product_needs_an_admin()
    {
        var product = await AnySeededProductAsync();
        var customer = await SignedInAsync("customer");

        (await Client.DeleteProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.Unauthorized);
        (await customer.DeleteProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.Forbidden);

        Assert.Contains((await Client.GetProductsAsync(Ct)).RequiredBody, p => p.Id == product.Id);
    }

    [Fact]
    public async Task Admin_deletes_a_product_and_deleting_it_again_is_still_204()
    {
        var admin = await SignedInAsync("admin");
        var product = await AnySeededProductAsync();

        (await admin.DeleteProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.NoContent);
        (await admin.DeleteProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.NoContent);

        Assert.DoesNotContain((await Client.GetProductsAsync(Ct)).RequiredBody, p => p.Id == product.Id);
        (await Client.GetProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_product_that_never_existed_is_404()
    {
        var admin = await SignedInAsync("admin");

        (await admin.DeleteProductAsync(Guid.NewGuid(), Ct)).ShouldHaveStatus(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_product_does_not_rewrite_orders_already_placed()
    {
        var admin = await SignedInAsync("admin");
        var product = await AnySeededProductAsync();
        var order = (await Client.PlaceOrderAsync(OrderOf(product, 2, "tok_success"), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;

        (await admin.DeleteProductAsync(product.Id, Ct)).ShouldHaveStatus(HttpStatusCode.NoContent);

        // The old order still shows what was bought, at the price paid.
        var retrieved = (await Client.GetOrderAsync(order.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        var line = Assert.Single(retrieved.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(product.Name, line.ProductName);
        Assert.Equal(product.Price, line.UnitPrice);
        Assert.Equal(order.Total, retrieved.Total);

        // But nobody can buy it any more.
        var again = await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct);
        again.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_garbage_token_is_unauthorized()
    {
        var client = new StorefrontClient(Api.CreateClient());
        client.SignIn("not.a.token");

        (await client.CreateProductAsync(NewHouse, Ct)).ShouldHaveStatus(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("nobody", "nobody")]
    public async Task Wrong_credentials_get_no_token(string user, string password)
    {
        (await Client.GetTokenAsync(user, password, Ct)).ShouldHaveStatus(HttpStatusCode.Unauthorized);
    }
}

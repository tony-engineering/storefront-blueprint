using System.Net;
using Storefront.Api.Persistence;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;

namespace Storefront.IntegrationTests.Orders;

/// <summary>Creating and retrieving orders: validation, pricing, unknown and deleted products, payable states.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class OrderApiTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Retrieved_order_matches_the_created_one()
    {
        var products = (await Client.GetProductsAsync(Ct)).RequiredBody;
        var request = new CreateOrderRequest(
            [new OrderLineRequest(products[0].Id, 2), new OrderLineRequest(products[1].Id, 1)],
            "tok_success");

        var created = (await Client.PlaceOrderAsync(request, Ct)).ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;
        var retrieved = (await Client.GetOrderAsync(created.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;

        Assert.Equal(created, retrieved with { Lines = created.Lines, Payments = created.Payments });
        Assert.Equal(
            [products[0].Id, products[1].Id],
            retrieved.Lines.Select(l => l.ProductId).ToArray()); // in the order they were placed
        Assert.Equal(products[0].Price.AmountMinor * 2 + products[1].Price.AmountMinor, retrieved.Total.AmountMinor);
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        var response = await Client.GetOrderAsync(Guid.NewGuid(), Ct);

        response.ShouldHaveStatus(HttpStatusCode.NotFound);
        Assert.Equal("/errors/not-found", response.RequiredProblem.Type);
    }

    [Fact]
    public async Task Prices_sent_by_the_client_are_ignored()
    {
        var product = await AnySeededProductAsync();
        var json = $$"""
            {"lines":[{"productId":"{{product.Id}}","quantity":2,"unitPrice":{"amountMinor":1,"currency":"GBP"},"productName":"Free house"}],
             "total":{"amountMinor":1,"currency":"GBP"},"paymentToken":"tok_success"}
            """;

        var order = (await Client.PlaceOrderJsonAsync(json, Guid.NewGuid().ToString(), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;

        Assert.Equal(product.Price.AmountMinor * 2, order.Total.AmountMinor);
        var line = Assert.Single(order.Lines);
        Assert.Equal(product.Price, line.UnitPrice);
        Assert.Equal(product.Name, line.ProductName);
        Assert.Equal(product.Price.AmountMinor * 2, Assert.Single(Gateway.Charges).AmountMinor);
    }

    [Fact]
    public async Task Order_without_lines_is_rejected_with_a_field_error()
    {
        var response = await Client.PlaceOrderAsync(new CreateOrderRequest([], "tok_success"), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Equal("/errors/validation", response.RequiredProblem.Type);
        Assert.Contains("lines", response.RequiredProblem.Errors!.Keys);
        Assert.Empty(Gateway.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Quantity_outside_1_to_100_is_rejected(int quantity)
    {
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, quantity, "tok_success"), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Contains("lines[0].quantity", response.RequiredProblem.Errors!.Keys);
    }

    [Fact]
    public async Task More_than_50_lines_are_rejected()
    {
        var lines = Enumerable.Range(0, 51).Select(_ => new OrderLineRequest(Guid.NewGuid(), 1)).ToList();

        var response = await Client.PlaceOrderAsync(new CreateOrderRequest(lines, "tok_success"), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Contains("lines", response.RequiredProblem.Errors!.Keys);
    }

    [Fact]
    public async Task The_same_product_twice_is_rejected()
    {
        var product = await AnySeededProductAsync();
        var request = new CreateOrderRequest([new OrderLineRequest(product.Id, 1), new OrderLineRequest(product.Id, 2)], "tok_success");

        var response = await Client.PlaceOrderAsync(request, Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Contains("lines", response.RequiredProblem.Errors!.Keys);
    }

    [Fact]
    public async Task Missing_payment_token_is_rejected()
    {
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, 1, ""), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Contains("paymentToken", response.RequiredProblem.Errors!.Keys);
    }

    [Fact]
    public async Task A_number_sent_as_text_is_rejected()
    {
        var product = await AnySeededProductAsync();
        var json = $$"""{"lines":[{"productId":"{{product.Id}}","quantity":"2"}],"paymentToken":"tok_success"}""";

        var response = await Client.PlaceOrderJsonAsync(json, Guid.NewGuid().ToString(), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Empty(Gateway.Calls);
    }

    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        var response = await Client.PlaceOrderJsonAsync("{ not json", Guid.NewGuid().ToString(), Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_product_is_rejected_and_nothing_is_charged()
    {
        var missing = Guid.NewGuid();

        var response = await Client.PlaceOrderAsync(
            new CreateOrderRequest([new OrderLineRequest(missing, 1)], "tok_success"), Ct);

        response.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("/errors/product-unavailable", response.RequiredProblem.Type);
        Assert.Contains(missing.ToString(), response.RawBody);
        Assert.Empty(Gateway.Calls);
    }

    [Fact]
    public async Task Deleted_product_cannot_be_ordered()
    {
        // Seeded already soft-deleted.
        var deleted = new CreateOrderRequest([new OrderLineRequest(DbSeeder.ProductIds.Prototype, 1)], "tok_success");

        var response = await Client.PlaceOrderAsync(deleted, Ct);

        response.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("/errors/product-unavailable", response.RequiredProblem.Type);
        Assert.Empty(Gateway.Calls);
    }

    [Fact]
    public async Task A_rejected_order_is_replayed_for_the_same_key_too()
    {
        var key = Guid.NewGuid();
        var request = new CreateOrderRequest([new OrderLineRequest(Guid.NewGuid(), 1)], "tok_success");

        var first = await Client.PlaceOrderAsync(request, Ct, key);
        var second = await Client.PlaceOrderAsync(request, Ct, key);

        first.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
        second.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
        Assert.True(second.WasReplayed);
    }

    [Fact]
    public async Task A_paid_order_cannot_be_paid_again()
    {
        var product = await AnySeededProductAsync();
        var paid = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;

        var response = await Client.RetryPaymentAsync(paid.Id, "tok_success", Ct);

        response.ShouldHaveStatus(HttpStatusCode.Conflict);
        Assert.Equal("/errors/order-not-payable", response.RequiredProblem.Type);
        Assert.Single(Gateway.Charges);
    }

    [Fact]
    public async Task Paying_an_unknown_order_is_404()
    {
        var response = await Client.RetryPaymentAsync(Guid.NewGuid(), "tok_success", Ct);

        response.ShouldHaveStatus(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Paying_again_needs_a_payment_token()
    {
        var product = await AnySeededProductAsync();
        Gateway.Decline("tok_decline", "card_declined");
        var declined = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_decline"), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;

        var response = await Client.RetryPaymentAsync(declined.Id, "", Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
    }
}

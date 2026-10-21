using System.Net;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;

namespace Storefront.IntegrationTests.Orders;

/// <summary>
/// Repeated requests (ARCHITECTURE.md §6): a double click, a network retry or a refresh must never create a
/// second order or take a second payment, and every outcome must be predictable.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class IdempotencyTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Same_key_twice_creates_one_order_and_replays_the_first_response()
    {
        var product = await AnySeededProductAsync();
        var key = Guid.NewGuid();
        var request = OrderOf(product, quantity: 1, "tok_success");

        var first = await Client.PlaceOrderAsync(request, Ct, key);
        var second = await Client.PlaceOrderAsync(request, Ct, key);

        first.ShouldHaveStatus(HttpStatusCode.Created);
        second.ShouldHaveStatus(HttpStatusCode.Created);
        Assert.False(first.WasReplayed);
        Assert.True(second.WasReplayed);

        // Same status, same body, same order.
        Assert.Equal(first.RawBody, second.RawBody);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(first.RequiredBody.Id, second.RequiredBody.Id);

        // The customer was charged once.
        Assert.Single(Gateway.Calls);
        Assert.Single(Gateway.Charges);
    }

    [Fact]
    public async Task Same_key_is_replayed_even_if_the_order_has_changed_since()
    {
        // The stored response is what the customer was told then, not today's state of the order.
        Gateway.StartOutage(TimeSpan.FromSeconds(30));
        var product = await AnySeededProductAsync();
        var key = Guid.NewGuid();
        var request = OrderOf(product, quantity: 1, "tok_success");

        var first = (await Client.PlaceOrderAsync(request, Ct, key)).ShouldHaveStatus(HttpStatusCode.Accepted);
        await LetTimePassAsync(TimeSpan.FromMinutes(2), step: TimeSpan.FromSeconds(1));
        var order = (await Client.GetOrderAsync(first.RequiredBody.Id, Ct)).RequiredBody;
        Assert.Equal(OrderStatuses.Paid, order.Status);

        var replay = await Client.PlaceOrderAsync(request, Ct, key);

        replay.ShouldHaveStatus(HttpStatusCode.Accepted);
        Assert.True(replay.WasReplayed);
        Assert.Equal(first.RawBody, replay.RawBody);
        Assert.Single(Gateway.Charges);
    }

    [Fact]
    public async Task Ten_parallel_requests_with_one_key_create_exactly_one_order()
    {
        var product = await AnySeededProductAsync();
        var key = Guid.NewGuid();
        var request = OrderOf(product, quantity: 1, "tok_success");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => Task.Run(() => Client.PlaceOrderAsync(request, Ct, key), Ct)));

        // Each caller gets either the order (created or replayed) or "still in progress, try again".
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected {(int)r.StatusCode}: {r.RawBody}"));

        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var inProgress = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        // Exactly one request did the work; any other 201 is a replay of it.
        Assert.Single(created, r => !r.WasReplayed);
        Assert.Single(created.Select(r => r.RequiredBody.Id).Distinct());
        Assert.All(inProgress, r =>
        {
            Assert.Equal("/errors/request-in-progress", r.RequiredProblem.Type);
            Assert.Equal(TimeSpan.FromSeconds(1), r.Headers.RetryAfter?.Delta);
        });

        // One order, one call to the provider, one charge.
        Assert.Single(Gateway.Calls);
        Assert.Single(Gateway.Charges);

        // Once the dust settles, the same key still answers with that order.
        var again = (await Client.PlaceOrderAsync(request, Ct, key)).ShouldHaveStatus(HttpStatusCode.Created);
        Assert.True(again.WasReplayed);
        Assert.Equal(created[0].RequiredBody.Id, again.RequiredBody.Id);
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_rejected_and_nothing_is_charged_again()
    {
        var product = await AnySeededProductAsync();
        var key = Guid.NewGuid();

        (await Client.PlaceOrderAsync(OrderOf(product, quantity: 1, "tok_success"), Ct, key))
            .ShouldHaveStatus(HttpStatusCode.Created);
        var different = await Client.PlaceOrderAsync(OrderOf(product, quantity: 2, "tok_success"), Ct, key);

        different.ShouldHaveStatus(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("/errors/idempotency-key-reused", different.RequiredProblem.Type);
        Assert.Single(Gateway.Calls);
    }

    [Fact]
    public async Task Missing_key_is_rejected()
    {
        var product = await AnySeededProductAsync();
        var json = $$"""{"lines":[{"productId":"{{product.Id}}","quantity":1}],"paymentToken":"tok_success"}""";

        var response = await Client.PlaceOrderJsonAsync(json, idempotencyKey: null, Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Equal("/errors/idempotency-key-invalid", response.RequiredProblem.Type);
        Assert.Empty(Gateway.Calls);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Key_that_is_not_a_real_uuid_is_rejected(string key)
    {
        var product = await AnySeededProductAsync();
        var json = $$"""{"lines":[{"productId":"{{product.Id}}","quantity":1}],"paymentToken":"tok_success"}""";

        var response = await Client.PlaceOrderJsonAsync(json, key, Ct);

        response.ShouldHaveStatus(HttpStatusCode.BadRequest);
        Assert.Equal("/errors/idempotency-key-invalid", response.RequiredProblem.Type);
        Assert.Empty(Gateway.Calls);
    }

    [Fact]
    public async Task Paying_again_twice_with_the_same_key_charges_once()
    {
        Gateway.Decline("tok_decline", "card_declined");
        var product = await AnySeededProductAsync();
        var declined = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_decline"), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;
        var key = Guid.NewGuid();

        var first = await Client.RetryPaymentAsync(declined.Id, "tok_success", Ct, key);
        var second = await Client.RetryPaymentAsync(declined.Id, "tok_success", Ct, key);

        first.ShouldHaveStatus(HttpStatusCode.Created);
        second.ShouldHaveStatus(HttpStatusCode.Created);
        Assert.True(second.WasReplayed);
        Assert.Equal(first.RawBody, second.RawBody);
        Assert.Single(Gateway.Charges);
        Assert.Equal(2, Gateway.Calls.Count); // the decline, then one successful charge
    }

    [Fact]
    public async Task Paying_again_in_parallel_with_different_keys_starts_only_one_new_attempt()
    {
        Gateway.Decline("tok_decline", "card_declined");
        var product = await AnySeededProductAsync();
        var declined = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_decline"), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => Task.Run(() => Client.RetryPaymentAsync(declined.Id, "tok_success", Ct), Ct)));

        // One click wins; the others are told the order is no longer payable. The customer pays once.
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r =>
            {
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Equal("/errors/order-not-payable", r.RequiredProblem.Type);
            });
        Assert.Single(Gateway.Charges);

        var order = (await Client.GetOrderAsync(declined.Id, Ct)).RequiredBody;
        Assert.Equal(OrderStatuses.Paid, order.Status);
        Assert.Equal(2, order.Payments.Count);
    }
}

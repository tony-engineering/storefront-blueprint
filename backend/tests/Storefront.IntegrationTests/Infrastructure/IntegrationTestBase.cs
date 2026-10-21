using System.Net;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>
/// Gives every test an empty, freshly seeded database and its own API instance,
/// so its clock and payment provider script cannot leak into other tests.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture database) : IAsyncLifetime
{
    protected StorefrontApiFactory Api { get; private set; } = null!;

    protected StorefrontClient Client { get; private set; } = null!;

    protected FakeTimeProvider Clock => Api.Clock;

    protected ScriptedPaymentGateway Gateway => Api.Gateway;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetDatabaseAsync();
        Api = new StorefrontApiFactory(database.ConnectionString);
        Client = new StorefrontClient(Api.CreateClient());
    }

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();

    protected async Task<ProductDto> AnySeededProductAsync()
    {
        var products = (await Client.GetProductsAsync(Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        Assert.NotEmpty(products);
        return products[0];
    }

    /// <summary>A client holding a token for one of the demo users ("admin" or "customer").</summary>
    protected async Task<StorefrontClient> SignedInAsync(string user)
    {
        var client = new StorefrontClient(Api.CreateClient());
        var token = (await client.GetTokenAsync(user, user, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        client.SignIn(token.AccessToken);
        return client;
    }

    protected static CreateOrderRequest OrderOf(ProductDto product, int quantity, string paymentToken) =>
        new([new OrderLineRequest(product.Id, quantity)], paymentToken);

    /// <summary>
    /// Lets simulated time pass one second at a time. Each second the reconciler runs (as the
    /// background loop would), and every <paramref name="pollEvery"/> the customer's browser
    /// polls the order. Stops as soon as <paramref name="until"/> holds for the polled order.
    /// </summary>
    protected async Task<OrderDto> PollOrderAsync(
        Guid orderId, Func<OrderDto, bool> until, TimeSpan pollEvery, TimeSpan giveUpAfter)
    {
        var deadline = Clock.GetUtcNow() + giveUpAfter;
        var nextPoll = Clock.GetUtcNow() + pollEvery;
        OrderDto? order = null;

        while (Clock.GetUtcNow() < deadline)
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            await Api.RunReconcilerAsync(Ct);

            if (Clock.GetUtcNow() < nextPoll)
            {
                continue;
            }

            nextPoll += pollEvery;
            order = (await Client.GetOrderAsync(orderId, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
            if (until(order))
            {
                return order;
            }
        }

        Assert.Fail($"Order {orderId} did not reach the expected state within {giveUpAfter} of simulated time. " +
                    $"Last status seen: {order?.Status ?? "(never polled)"}.");
        return order!;
    }

    /// <summary>Lets simulated time pass, running the reconciler every <paramref name="step"/>.</summary>
    protected async Task LetTimePassAsync(TimeSpan duration, TimeSpan step)
    {
        var end = Clock.GetUtcNow() + duration;
        while (Clock.GetUtcNow() < end)
        {
            Clock.Advance(step);
            await Api.RunReconcilerAsync(Ct);
        }
    }
}

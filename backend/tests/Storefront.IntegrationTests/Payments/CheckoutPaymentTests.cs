using System.Net;
using Storefront.Api.Payments;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;
using static Storefront.IntegrationTests.Infrastructure.StorefrontApiFactory;

namespace Storefront.IntegrationTests.Payments;

/// <summary>
/// A customer places an order and pays for it, end to end through HTTP, against a real
/// PostgreSQL and a scripted payment provider. Time is simulated, so outages of seconds
/// or hours run in a few seconds of wall-clock time.
///
/// The property every scenario checks, whatever happens: the customer is charged at most
/// once, exactly the order total, and is never told "paid" or "not charged" unless that is
/// true at the provider.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CheckoutPaymentTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string GoodCard = "tok_success";
    private const string DeclinedCard = "tok_decline";
    private const string DeclineCode = "card_declined";

    /// <summary>How often the checkout page polls an order whose payment is still being retried.</summary>
    private static readonly TimeSpan BrowserPollInterval = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Happy_path_order_is_paid_on_the_first_attempt()
    {
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, quantity: 2, GoodCard), Ct);

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var order = response.RequiredBody;
        Assert.EndsWith($"/api/orders/{order.Id}", response.Headers.Location?.OriginalString ?? "");
        Assert.Equal(OrderStatuses.Paid, order.Status);
        Assert.True(order.Charged);
        Assert.Null(order.FailureReason);
        Assert.Equal(product.Price.AmountMinor * 2, order.Total.AmountMinor);

        var attempt = Assert.Single(order.Payments);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(PaymentAttemptStatuses.Succeeded, attempt.Status);

        // Charged once, for exactly the order total.
        Assert.Single(Gateway.Calls);
        var charge = Assert.Single(Gateway.Charges);
        Assert.Equal(order.Id, charge.OrderId);
        Assert.Equal(order.Total.AmountMinor, charge.AmountMinor);
        Assert.Equal(order.Total.Currency, charge.Currency);

        // The confirmation is durable: retrieving the order later says the same.
        var retrieved = (await Client.GetOrderAsync(order.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        Assert.Equal(OrderStatuses.Paid, retrieved.Status);
        Assert.True(retrieved.Charged);
    }

    [Fact]
    public async Task Declined_card_without_retry_leaves_the_order_failed_and_uncharged()
    {
        Gateway.Decline(DeclinedCard, DeclineCode);
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, quantity: 1, DeclinedCard), Ct);

        // The order exists (201); its payment was declined.
        response.ShouldHaveStatus(HttpStatusCode.Created);
        var order = response.RequiredBody;
        Assert.Equal(OrderStatuses.PaymentFailed, order.Status);
        Assert.False(order.Charged);
        Assert.Equal(DeclineCode, order.FailureReason);

        var attempt = Assert.Single(order.Payments);
        Assert.Equal(PaymentAttemptStatuses.Declined, attempt.Status);
        Assert.Equal(DeclineCode, attempt.FailureReason);

        // The customer walks away. A decline is a final answer from the bank, so the system
        // must never retry it on its own, however much time passes.
        await LetTimePassAsync(TimeSpan.FromMinutes(30), step: TimeSpan.FromSeconds(10));

        var retrieved = (await Client.GetOrderAsync(order.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        Assert.Equal(OrderStatuses.PaymentFailed, retrieved.Status);
        Assert.False(retrieved.Charged);
        Assert.Single(retrieved.Payments);

        Assert.Single(Gateway.Calls);
        Assert.Empty(Gateway.Charges);
    }

    [Fact]
    public async Task Declined_card_then_retry_with_another_card_pays_the_same_order_once()
    {
        Gateway.Decline(DeclinedCard, DeclineCode);
        var product = await AnySeededProductAsync();

        var declined = (await Client.PlaceOrderAsync(OrderOf(product, quantity: 1, DeclinedCard), Ct))
            .ShouldHaveStatus(HttpStatusCode.Created)
            .RequiredBody;
        Assert.Equal(OrderStatuses.PaymentFailed, declined.Status);

        // Same order, second payment attempt with a different card (and a new Idempotency-Key).
        var retry = await Client.RetryPaymentAsync(declined.Id, GoodCard, Ct);

        retry.ShouldHaveStatus(HttpStatusCode.Created);
        var paid = retry.RequiredBody;
        Assert.Equal(declined.Id, paid.Id);
        Assert.Equal(OrderStatuses.Paid, paid.Status);
        Assert.True(paid.Charged);
        Assert.Null(paid.FailureReason);
        Assert.Equal(declined.Total, paid.Total);

        // Both attempts are kept, in order: the history explains what happened.
        Assert.Collection(
            paid.Payments,
            first =>
            {
                Assert.Equal(1, first.Number);
                Assert.Equal(PaymentAttemptStatuses.Declined, first.Status);
                Assert.Equal(DeclineCode, first.FailureReason);
            },
            second =>
            {
                Assert.Equal(2, second.Number);
                Assert.Equal(PaymentAttemptStatuses.Succeeded, second.Status);
            });

        // One charge, on the second card, for the order total. Each attempt used its own
        // idempotency key at the provider, otherwise the provider would replay the decline.
        var charge = Assert.Single(Gateway.Charges);
        Assert.Equal(GoodCard, charge.PaymentToken);
        Assert.Equal(paid.Total.AmountMinor, charge.AmountMinor);

        Assert.Equal(2, Gateway.Calls.Count);
        Assert.Equal(2, Gateway.Calls.Select(call => call.IdempotencyKey).Distinct().Count());

        var retrieved = (await Client.GetOrderAsync(paid.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        Assert.Equal(OrderStatuses.Paid, retrieved.Status);
    }

    [Fact]
    public async Task Provider_down_for_30_seconds_payment_is_retried_and_the_customer_gets_a_confirmation()
    {
        Gateway.StartOutage(TimeSpan.FromSeconds(30));
        var outageEndsAt = Gateway.OutageEndsAt;
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, quantity: 1, GoodCard), Ct);

        // Not a failure and not a success yet: accepted, payment will be retried.
        response.ShouldHaveStatus(HttpStatusCode.Accepted);
        var pending = response.RequiredBody;
        Assert.EndsWith($"/api/orders/{pending.Id}", response.Headers.Location?.OriginalString ?? "");
        Assert.Equal(OrderStatuses.PaymentRetrying, pending.Status);
        Assert.False(pending.Charged);
        Assert.Equal(PaymentAttemptStatuses.Retrying, Assert.Single(pending.Payments).Status);

        // The checkout page polls while the system retries in the background.
        var confirmed = await PollOrderAsync(
            pending.Id,
            until: order => order.Status != OrderStatuses.PaymentRetrying,
            pollEvery: BrowserPollInterval,
            giveUpAfter: TimeSpan.FromMinutes(5));

        Assert.Equal(OrderStatuses.Paid, confirmed.Status);
        Assert.True(confirmed.Charged);
        Assert.Null(confirmed.FailureReason);
        Assert.True(Clock.GetUtcNow() >= outageEndsAt, "The order cannot be paid before the provider is back.");

        // The outage retried the same attempt; it did not create new ones.
        var attempt = Assert.Single(confirmed.Payments);
        Assert.Equal(PaymentAttemptStatuses.Succeeded, attempt.Status);

        // Several calls, all with one idempotency key, ending in exactly one charge.
        var calls = Gateway.Calls;
        Assert.InRange(calls.Count, 2, RetryPolicy.MaxChargeAttempts);
        Assert.Single(calls.Select(call => call.IdempotencyKey).Distinct());
        Assert.All(calls.SkipLast(1), call => Assert.Equal(ChargeOutcome.Unavailable, call.Outcome));
        Assert.Equal(ChargeOutcome.Succeeded, calls[^1].Outcome);

        var charge = Assert.Single(Gateway.Charges);
        Assert.Equal(confirmed.Total.AmountMinor, charge.AmountMinor);
    }

    [Fact]
    public async Task Provider_down_for_too_long_system_stops_retrying_and_tells_the_customer_they_were_not_charged()
    {
        Gateway.StartOutage(TimeSpan.FromHours(2));
        var product = await AnySeededProductAsync();
        var placedAt = Clock.GetUtcNow();

        var response = await Client.PlaceOrderAsync(OrderOf(product, quantity: 1, GoodCard), Ct);

        response.ShouldHaveStatus(HttpStatusCode.Accepted);
        var pending = response.RequiredBody;
        Assert.Equal(OrderStatuses.PaymentRetrying, pending.Status);
        Assert.False(pending.Charged);

        var final = await PollOrderAsync(
            pending.Id,
            until: order => order.Status != OrderStatuses.PaymentRetrying,
            pollEvery: BrowserPollInterval,
            giveUpAfter: TimeSpan.FromMinutes(15));

        // A definite answer the client can show: the provider is unavailable, nothing was charged.
        Assert.Equal(OrderStatuses.PaymentUnavailable, final.Status);
        Assert.False(final.Charged);
        Assert.Equal(FailureReasons.ProviderUnavailable, final.FailureReason);

        var attempt = Assert.Single(final.Payments);
        Assert.Equal(PaymentAttemptStatuses.ProviderUnavailable, attempt.Status);
        Assert.Equal(FailureReasons.ProviderUnavailable, attempt.FailureReason);

        // The customer hears back within a couple of minutes, not when the outage ends.
        Assert.True(
            Clock.GetUtcNow() - placedAt <= TimeSpan.FromMinutes(2),
            $"The customer waited {Clock.GetUtcNow() - placedAt} of simulated time for an answer.");

        // The retry limit was hit exactly, every call was refused, and no money moved.
        var calls = Gateway.Calls;
        Assert.Equal(RetryPolicy.MaxChargeAttempts, calls.Count);
        Assert.All(calls, call => Assert.Equal(ChargeOutcome.Unavailable, call.Outcome));
        Assert.Single(calls.Select(call => call.IdempotencyKey).Distinct());
        Assert.Empty(Gateway.Charges);

        // Having given up, the system stays quiet: no more calls, the answer does not change.
        await LetTimePassAsync(TimeSpan.FromMinutes(30), step: TimeSpan.FromSeconds(10));

        Assert.Equal(RetryPolicy.MaxChargeAttempts, Gateway.Calls.Count);
        var later = (await Client.GetOrderAsync(pending.Id, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;
        Assert.Equal(OrderStatuses.PaymentUnavailable, later.Status);
        Assert.False(later.Charged);
    }
}

using System.Net;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;

namespace Storefront.IntegrationTests.Payments;

/// <summary>
/// The provider times out, so nobody knows whether the customer was charged (ARCHITECTURE.md §7).
/// The system must answer "accepted, pending" (never "failed"), never charge again, and settle the order only
/// when the provider says what happened.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class UnknownOutcomeTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string SlowCard = "tok_timeout";
    private static readonly TimeSpan BrowserPollInterval = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Timeout_where_the_charge_went_through_ends_paid_without_charging_twice()
    {
        Gateway.Timeout(SlowCard, chargeGoesThrough: true);
        var product = await AnySeededProductAsync();

        var response = await Client.PlaceOrderAsync(OrderOf(product, 1, SlowCard), Ct);

        // Not "failed": paying again could take the money twice.
        response.ShouldHaveStatus(HttpStatusCode.Accepted);
        var pending = response.RequiredBody;
        Assert.Equal(OrderStatuses.PaymentUnknown, pending.Status);
        Assert.False(pending.Charged);
        Assert.Equal(PaymentAttemptStatuses.Unknown, Assert.Single(pending.Payments).Status);

        var settled = await PollOrderAsync(
            pending.Id, o => o.Status != OrderStatuses.PaymentUnknown, BrowserPollInterval, TimeSpan.FromMinutes(5));

        Assert.Equal(OrderStatuses.Paid, settled.Status);
        Assert.True(settled.Charged);
        Assert.Equal(PaymentAttemptStatuses.Succeeded, Assert.Single(settled.Payments).Status);

        // The charge call was made once and never repeated; the status lookup found the charge.
        Assert.Single(Gateway.Calls);
        Assert.Single(Gateway.Charges);
    }

    [Fact]
    public async Task Timeout_where_nothing_was_charged_fails_and_the_customer_can_pay_again()
    {
        Gateway.Timeout(SlowCard, chargeGoesThrough: false);
        var product = await AnySeededProductAsync();
        var pending = (await Client.PlaceOrderAsync(OrderOf(product, 1, SlowCard), Ct))
            .ShouldHaveStatus(HttpStatusCode.Accepted).RequiredBody;

        var settled = await PollOrderAsync(
            pending.Id, o => o.Status != OrderStatuses.PaymentUnknown, BrowserPollInterval, TimeSpan.FromMinutes(5));

        Assert.Equal(OrderStatuses.PaymentFailed, settled.Status);
        Assert.False(settled.Charged);
        Assert.Equal(FailureReasons.NotProcessed, settled.FailureReason);
        Assert.Equal(PaymentAttemptStatuses.Failed, Assert.Single(settled.Payments).Status);
        Assert.Empty(Gateway.Charges);

        // The provider confirmed nothing was taken, so paying again with a working card is safe.
        var paid = (await Client.RetryPaymentAsync(pending.Id, "tok_success", Ct))
            .ShouldHaveStatus(HttpStatusCode.Created).RequiredBody;
        Assert.Equal(OrderStatuses.Paid, paid.Status);
        Assert.Single(Gateway.Charges);
    }

    [Fact]
    public async Task While_the_outcome_is_unknown_the_order_cannot_be_paid_again()
    {
        Gateway.Timeout(SlowCard, chargeGoesThrough: true);
        var product = await AnySeededProductAsync();
        var pending = (await Client.PlaceOrderAsync(OrderOf(product, 1, SlowCard), Ct))
            .ShouldHaveStatus(HttpStatusCode.Accepted).RequiredBody;

        var response = await Client.RetryPaymentAsync(pending.Id, "tok_success", Ct);

        response.ShouldHaveStatus(HttpStatusCode.Conflict);
        Assert.Equal("/errors/order-not-payable", response.RequiredProblem.Type);
    }

    [Fact]
    public async Task When_the_provider_never_says_what_happened_a_person_is_asked_to_look()
    {
        Gateway.Timeout(SlowCard, chargeGoesThrough: true);
        var product = await AnySeededProductAsync();
        var pending = (await Client.PlaceOrderAsync(OrderOf(product, 1, SlowCard), Ct))
            .ShouldHaveStatus(HttpStatusCode.Accepted).RequiredBody;

        // From now on the provider cannot be asked.
        Gateway.StartOutage(TimeSpan.FromHours(3));

        var stuck = await PollOrderAsync(
            pending.Id, o => o.Status != OrderStatuses.PaymentUnknown, BrowserPollInterval, TimeSpan.FromMinutes(30));

        Assert.Equal(OrderStatuses.NeedsAttention, stuck.Status);
        Assert.False(stuck.Charged); // we do not claim a charge we cannot confirm

        // It is left alone: no more lookups, no new charge, and no second payment either.
        await LetTimePassAsync(TimeSpan.FromMinutes(30), step: TimeSpan.FromSeconds(10));
        Assert.Single(Gateway.Calls);
        (await Client.RetryPaymentAsync(pending.Id, "tok_success", Ct)).ShouldHaveStatus(HttpStatusCode.Conflict);
    }
}

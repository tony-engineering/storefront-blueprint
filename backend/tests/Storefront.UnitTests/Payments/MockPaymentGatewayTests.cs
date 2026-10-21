using Storefront.Api.Payments;
using Xunit;

namespace Storefront.UnitTests.Payments;

/// <summary>
/// The six tokens documented in ARCHITECTURE.md §7 and the API docs. The integration tests swap this gateway for a
/// scripted one, so this is where the mock's own promises are checked.
/// </summary>
public sealed class MockPaymentGatewayTests
{
    private static ChargeRequest Charge(string token, string key = "attempt-1") =>
        new(key, Guid.NewGuid(), 1999, "GBP", token);

    [Fact]
    public async Task Success_charges_and_reports_the_charge()
    {
        var gateway = new MockPaymentGateway();

        var result = await gateway.ChargeAsync(Charge("tok_success"), CancellationToken.None);

        Assert.Equal(ChargeOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.ChargeId);
        var status = await gateway.GetStatusAsync("attempt-1", CancellationToken.None);
        Assert.Equal(new ChargeStatusResult(ChargeStatus.Charged, result.ChargeId), status);
    }

    [Fact]
    public async Task Repeating_a_charge_with_the_same_key_does_not_charge_twice()
    {
        var gateway = new MockPaymentGateway();

        var first = await gateway.ChargeAsync(Charge("tok_success"), CancellationToken.None);
        var second = await gateway.ChargeAsync(Charge("tok_success"), CancellationToken.None);

        Assert.Equal(first.ChargeId, second.ChargeId);
    }

    [Fact]
    public async Task A_new_key_is_a_new_charge()
    {
        var gateway = new MockPaymentGateway();

        var first = await gateway.ChargeAsync(Charge("tok_success", "attempt-1"), CancellationToken.None);
        var second = await gateway.ChargeAsync(Charge("tok_success", "attempt-2"), CancellationToken.None);

        Assert.NotEqual(first.ChargeId, second.ChargeId);
    }

    [Fact]
    public async Task Decline_is_insufficient_funds_and_nothing_is_charged()
    {
        var gateway = new MockPaymentGateway();

        var result = await gateway.ChargeAsync(Charge("tok_decline"), CancellationToken.None);

        Assert.Equal(new ChargeResult(ChargeOutcome.Declined, DeclineCode: "insufficient_funds"), result);
        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Timeout_never_answers_but_the_charge_went_through()
    {
        var gateway = new MockPaymentGateway();
        using var callerGivesUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gateway.ChargeAsync(Charge("tok_timeout"), callerGivesUp.Token));

        var status = await gateway.GetStatusAsync("attempt-1", CancellationToken.None);
        Assert.Equal(ChargeStatus.Charged, status.Status);
    }

    [Fact]
    public async Task Error_answers_unknown_and_nothing_was_charged()
    {
        var gateway = new MockPaymentGateway();

        var result = await gateway.ChargeAsync(Charge("tok_error"), CancellationToken.None);

        Assert.Equal(ChargeOutcome.Unknown, result.Outcome);
        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Unavailable_is_refused_every_time_and_nothing_is_charged()
    {
        var gateway = new MockPaymentGateway();

        for (var call = 0; call < 3; call++)
        {
            var result = await gateway.ChargeAsync(Charge("tok_unavailable"), CancellationToken.None);
            Assert.Equal(ChargeOutcome.Unavailable, result.Outcome);
        }

        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Flaky_status_is_unknown_for_two_lookups_then_charged()
    {
        var gateway = new MockPaymentGateway();
        var result = await gateway.ChargeAsync(Charge("tok_flaky"), CancellationToken.None);
        Assert.Equal(ChargeOutcome.Unknown, result.Outcome);

        var statuses = new List<ChargeStatus>();
        for (var lookup = 0; lookup < 4; lookup++)
        {
            statuses.Add((await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
        }

        Assert.Equal(
            [ChargeStatus.Unknown, ChargeStatus.Unknown, ChargeStatus.Charged, ChargeStatus.Charged],
            statuses);
    }

    [Fact]
    public async Task An_unrecognised_token_is_declined()
    {
        var gateway = new MockPaymentGateway();

        var result = await gateway.ChargeAsync(Charge("tok_made_up"), CancellationToken.None);

        Assert.Equal(ChargeOutcome.Declined, result.Outcome);
        Assert.Equal("invalid_payment_token", result.DeclineCode);
    }

    [Fact]
    public async Task Looking_up_a_charge_that_was_never_made_says_not_charged()
    {
        var gateway = new MockPaymentGateway();

        var status = await gateway.GetStatusAsync("never-seen", CancellationToken.None);

        Assert.Equal(ChargeStatus.NotCharged, status.Status);
    }
}

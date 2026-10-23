using Microsoft.Extensions.Time.Testing;
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

    [Theory]
    [InlineData("tok_success", "tok_success", 0, 0)]
    [InlineData("tok_success@500", "tok_success", 500, 500)]
    [InlineData("tok_success@300-800", "tok_success", 300, 800)]
    [InlineData("tok_success@5000-60000", "tok_success", 5000, 10000)] // capped at 10 s
    public void Token_suffix_is_parsed(string token, string expectedBase, int minMs, int maxMs)
    {
        Assert.True(MockToken.TryParse(token, out var parsed));
        Assert.Equal(expectedBase, parsed.Base);
        Assert.Equal(TimeSpan.FromMilliseconds(minMs), parsed.MinDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(maxMs), parsed.MaxDelayRange);
    }

    [Theory]
    [InlineData("tok_success@")]
    [InlineData("tok_success@abc")]
    [InlineData("tok_success@800-300")]
    [InlineData("tok_success@-5")]
    [InlineData("tok_success@1-")]
    [InlineData("@500")]
    public void A_malformed_suffix_is_not_a_token(string token)
    {
        Assert.False(MockToken.TryParse(token, out _));
    }

    [Fact]
    public async Task A_malformed_suffix_is_declined_as_an_invalid_token()
    {
        var gateway = new MockPaymentGateway();

        var result = await gateway.ChargeAsync(Charge("tok_success@oops"), CancellationToken.None);

        Assert.Equal(new ChargeResult(ChargeOutcome.Declined, DeclineCode: "invalid_payment_token"), result);
        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Money_is_taken_before_the_delay_runs()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);

        var charge = gateway.ChargeAsync(Charge("tok_success@5000"), CancellationToken.None);

        Assert.False(charge.IsCompleted);
        // The caller is still waiting, yet a status lookup already says the money is gone.
        Assert.Equal(ChargeStatus.Charged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(ChargeOutcome.Succeeded, (await charge).Outcome);
    }

    [Fact]
    public async Task A_delay_longer_than_the_callers_timeout_leaves_a_charge_nobody_heard_about()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);
        using var callerGivesUp = new CancellationTokenSource();

        var charge = gateway.ChargeAsync(Charge("tok_success@4000"), callerGivesUp.Token);
        await callerGivesUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => charge);
        Assert.Equal(ChargeStatus.Charged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_decline_answers_only_after_the_delay_and_charges_nothing()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);

        var charge = gateway.ChargeAsync(Charge("tok_decline@1000"), CancellationToken.None);

        Assert.False(charge.IsCompleted);
        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("attempt-1", CancellationToken.None)).Status);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ChargeOutcome.Declined, (await charge).Outcome);
    }

    [Fact]
    public void Status_lookups_have_no_delay()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);
        _ = gateway.ChargeAsync(Charge("tok_success@5000"), CancellationToken.None);

        var status = gateway.GetStatusAsync("attempt-1", CancellationToken.None);

        Assert.True(status.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Charges_untouched_for_an_hour_are_forgotten()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);
        await gateway.ChargeAsync(Charge("tok_success", "old"), CancellationToken.None);

        clock.Advance(MockPaymentGateway.EntryLifetime + TimeSpan.FromMinutes(6));
        await gateway.ChargeAsync(Charge("tok_success", "new"), CancellationToken.None); // triggers the sweep

        Assert.Equal(ChargeStatus.NotCharged, (await gateway.GetStatusAsync("old", CancellationToken.None)).Status);
        Assert.Equal(ChargeStatus.Charged, (await gateway.GetStatusAsync("new", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_charge_that_is_still_being_looked_at_is_kept()
    {
        var clock = new FakeTimeProvider();
        var gateway = new MockPaymentGateway(clock);
        await gateway.ChargeAsync(Charge("tok_success", "busy"), CancellationToken.None);

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(ChargeStatus.Charged, (await gateway.GetStatusAsync("busy", CancellationToken.None)).Status);
            await gateway.ChargeAsync(Charge("tok_success", $"other-{i}"), CancellationToken.None);
        }
    }
}

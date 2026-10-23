using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Api.Features.Lab;
using Storefront.Api.Persistence;
using Storefront.IntegrationTests.Infrastructure;
using Xunit;

namespace Storefront.IntegrationTests.Lab;

/// <summary>Orders tagged by the load generator are counted per run, and the janitor removes only old lab orders.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class LabOrderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task A_tagged_order_is_counted_by_status_and_an_untagged_one_is_not()
    {
        var product = await AnySeededProductAsync();
        var run = Guid.NewGuid();
        Gateway.Decline("tok_decline", "insufficient_funds");

        (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct, labRun: run)).ShouldHaveStatus(HttpStatusCode.Created);
        (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_decline"), Ct, labRun: run)).ShouldHaveStatus(HttpStatusCode.Created);
        (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct)).ShouldHaveStatus(HttpStatusCode.Created);

        var stats = (await Client.GetLabStatsAsync(run, Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;

        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.ByStatus["Paid"]);
        Assert.Equal(1, stats.ByStatus["PaymentFailed"]);
    }

    [Fact]
    public async Task A_run_nobody_used_has_no_orders()
    {
        var stats = (await Client.GetLabStatsAsync(Guid.NewGuid(), Ct)).ShouldHaveStatus(HttpStatusCode.OK).RequiredBody;

        Assert.Equal(0, stats.Total);
        Assert.Empty(stats.ByStatus);
    }

    [Fact]
    public async Task A_tagged_order_replayed_with_the_same_key_is_still_one_order()
    {
        var product = await AnySeededProductAsync();
        var run = Guid.NewGuid();
        var key = Guid.NewGuid();

        var first = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct, key, run)).RequiredBody;
        var second = await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct, key, run);

        Assert.True(second.WasReplayed);
        Assert.Equal(first.Id, second.RequiredBody.Id);
        Assert.Equal(1, (await Client.GetLabStatsAsync(run, Ct)).RequiredBody.Total);
    }

    [Fact]
    public async Task The_janitor_deletes_only_old_lab_orders()
    {
        var product = await AnySeededProductAsync();
        var oldRun = Guid.NewGuid();
        var oldLab = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct, labRun: oldRun)).RequiredBody;
        var oldReal = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct)).RequiredBody;

        Clock.Advance(TimeSpan.FromHours(12));
        var recentLab = (await Client.PlaceOrderAsync(OrderOf(product, 1, "tok_success"), Ct, labRun: Guid.NewGuid())).RequiredBody;

        Clock.Advance(TimeSpan.FromHours(13)); // the first two are now 25 h old, the recent one 13 h
        var deleted = await Api.Services.GetRequiredService<LabOrderJanitor>().PurgeAsync(Ct);

        Assert.Equal(1, deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetOrderAsync(oldLab.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetOrderAsync(oldReal.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetOrderAsync(recentLab.Id, Ct)).StatusCode);
        Assert.Equal(0, (await Client.GetLabStatsAsync(oldRun, Ct)).RequiredBody.Total);

        // Lines, payment attempts and idempotency records of the deleted order are gone with it.
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StorefrontDbContext>();
        Assert.False(await db.PaymentAttempts.AnyAsync(a => a.OrderId == oldLab.Id, Ct));
        Assert.False(await db.IdempotencyRecords.AnyAsync(r => r.OrderId == oldLab.Id, Ct));
        Assert.True(await db.IdempotencyRecords.AnyAsync(r => r.OrderId == oldReal.Id, Ct));
    }
}

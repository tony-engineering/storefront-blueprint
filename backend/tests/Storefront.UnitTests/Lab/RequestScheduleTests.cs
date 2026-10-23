using Storefront.LoadLab;
using Xunit;

namespace Storefront.UnitTests.Lab;

public sealed class RequestScheduleTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void A_burst_is_all_due_at_once()
    {
        var schedule = new RequestSchedule.Burst(300);

        Assert.Equal(300, schedule.Total);
        Assert.Equal(300, schedule.DueAt(S(0)));
    }

    [Fact]
    public void A_constant_rate_releases_requests_evenly_and_stops_at_the_total()
    {
        var schedule = new RequestSchedule.Constant(10, S(60));

        Assert.Equal(600, schedule.Total);
        Assert.Equal(0, schedule.DueAt(S(0)));
        Assert.Equal(10, schedule.DueAt(S(1)));
        Assert.Equal(300, schedule.DueAt(S(30)));
        Assert.Equal(600, schedule.DueAt(S(60)));
        Assert.Equal(600, schedule.DueAt(S(600)));
    }

    [Fact]
    public void A_ramp_starts_slow_and_ends_fast()
    {
        var schedule = new RequestSchedule.Ramp(2, 40, S(120));

        Assert.Equal(2520, schedule.Total);
        Assert.Equal(0, schedule.DueAt(S(0)));
        Assert.Equal(2520, schedule.DueAt(S(120)));

        // The rate at t is From + (To − From) × t ÷ D, so the first minute carries far less than the second.
        var firstHalf = schedule.DueAt(S(60));
        Assert.Equal(2 * 60 + (38.0 / 240) * 3600, firstHalf, 1);
        Assert.True(firstHalf < schedule.Total - firstHalf);
    }

    [Fact]
    public void Due_requests_never_go_backwards()
    {
        foreach (RequestSchedule schedule in new RequestSchedule[]
                 { new RequestSchedule.Constant(7, S(33)), new RequestSchedule.Ramp(1, 9, S(50)) })
        {
            var previous = 0;
            for (var ms = 0; ms <= 60_000; ms += 50)
            {
                var due = schedule.DueAt(TimeSpan.FromMilliseconds(ms));
                Assert.InRange(due, previous, schedule.Total);
                previous = due;
            }
        }
    }

    [Fact]
    public void Every_test_in_the_catalog_has_a_mix_that_adds_up_to_100()
    {
        foreach (var test in LabTests.All)
        {
            Assert.Equal(100, test.Mix.Sum(m => m.Percent));
        }
    }

    [Fact]
    public void The_token_mix_follows_the_percentages()
    {
        var steady = LabTests.Find("steady")!;

        var counts = Enumerable.Range(0, steady.Requests).GroupBy(i => steady.ShareFor(i).Token).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(510, counts["tok_success"]);
        Assert.Equal(60, counts["tok_decline"]);
        Assert.Equal(30, counts["tok_timeout"]);
    }

    [Fact]
    public void The_restart_test_sends_60_seconds_of_orders_that_all_charge()
    {
        var test = LabTests.Find("restart-api")!;

        Assert.Equal("Sending requests over 60 seconds", test.Title);
        Assert.Equal(600, test.Requests);
        Assert.All(test.Mix, share => Assert.True(share.Charges));
    }

    [Fact]
    public void Retry_storm_sends_400_requests_with_20_keys()
    {
        var test = LabTests.Find("retry-storm")!;

        Assert.Equal(400, test.Requests);
        Assert.Equal(20, test.KeyCount);
    }

    [Fact]
    public void A_share_renders_the_token_the_mock_gateway_reads()
    {
        Assert.Equal("tok_success@200-800", new TokenShare("tok_success", 100, 200, 800).Render());
        Assert.Equal("tok_success@500", new TokenShare("tok_success", 100, 500, 500).Render());
        Assert.Equal("tok_timeout", new TokenShare("tok_timeout", 100, 0, 0).Render());
    }
}

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Storefront.LoadLab;
using Xunit;

namespace Storefront.UnitTests.Lab;

public sealed class RunCoordinatorTests
{
    private readonly FakeTimeProvider _clock = new();

    private RunCoordinator Coordinator(bool enabled = true) =>
        new(Options.Create(new LoadLabOptions { Enabled = enabled, Cooldown = TimeSpan.FromSeconds(60) }), _clock);

    [Fact]
    public void The_first_run_starts()
    {
        var result = Coordinator().TryStart("flash-sale");

        Assert.Equal(StartOutcome.Started, result.Outcome);
        Assert.Equal("flash-sale", result.Run!.Test.Id);
    }

    [Fact]
    public void A_second_run_is_refused_while_one_is_running()
    {
        var coordinator = Coordinator();
        coordinator.TryStart("steady");

        Assert.Equal(StartOutcome.Busy, coordinator.TryStart("flash-sale").Outcome);
        Assert.True(coordinator.Snapshot().Busy);
    }

    [Fact]
    public void After_a_run_there_is_a_cooldown_with_the_time_left()
    {
        var coordinator = Coordinator();
        var run = coordinator.TryStart("steady").Run!;
        coordinator.Complete(run);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var refused = coordinator.TryStart("steady");

        Assert.Equal(StartOutcome.CoolingDown, refused.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(40), refused.RetryAfter);
        Assert.False(coordinator.Snapshot().Busy);
    }

    [Fact]
    public void Once_the_cooldown_is_over_the_next_run_starts()
    {
        var coordinator = Coordinator();
        coordinator.Complete(coordinator.TryStart("steady").Run!);
        _clock.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(StartOutcome.Started, coordinator.TryStart("outage").Outcome);
    }

    [Fact]
    public void An_unknown_test_is_refused_without_taking_the_lock()
    {
        var coordinator = Coordinator();

        Assert.Equal(StartOutcome.UnknownTest, coordinator.TryStart("nope").Outcome);
        Assert.Equal(StartOutcome.Started, coordinator.TryStart("steady").Outcome);
    }

    [Fact]
    public void A_disabled_lab_starts_nothing()
    {
        Assert.Equal(StartOutcome.Disabled, Coordinator(enabled: false).TryStart("steady").Outcome);
    }

    [Fact]
    public void Only_the_last_five_runs_are_kept()
    {
        var coordinator = Coordinator();
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var run = coordinator.TryStart("steady").Run!;
            ids.Add(run.Id);
            coordinator.Complete(run);
            _clock.Advance(TimeSpan.FromMinutes(2));
        }

        Assert.Null(coordinator.Find(ids[0]));
        Assert.Null(coordinator.Find(ids[1]));
        Assert.NotNull(coordinator.Find(ids[2]));
        Assert.NotNull(coordinator.Find(ids[6]));
    }
}

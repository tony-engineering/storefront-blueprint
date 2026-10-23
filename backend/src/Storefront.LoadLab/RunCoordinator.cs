using Microsoft.Extensions.Options;

namespace Storefront.LoadLab;

public enum StartOutcome
{
    Started,
    UnknownTest,
    Busy,
    CoolingDown,
    Disabled,
}

public readonly record struct StartResult(StartOutcome Outcome, Run? Run = null, TimeSpan RetryAfter = default);

/// <summary>
/// Lets one run happen at a time, then a cooldown. The lock lives in memory, so the lab must run as one replica.
/// </summary>
public sealed class RunCoordinator(IOptions<LoadLabOptions> options, TimeProvider clock)
{
    private const int KeepRuns = 5;

    private readonly object _gate = new();
    private readonly List<Run> _runs = [];
    private Run? _active;
    private DateTimeOffset? _cooldownEnds;

    public StartResult TryStart(string testId)
    {
        if (!options.Value.Enabled)
        {
            return new StartResult(StartOutcome.Disabled);
        }

        if (LabTests.Find(testId) is not { } test)
        {
            return new StartResult(StartOutcome.UnknownTest);
        }

        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_active is not null)
            {
                return new StartResult(StartOutcome.Busy);
            }

            if (_cooldownEnds is { } ends && now < ends)
            {
                return new StartResult(StartOutcome.CoolingDown, RetryAfter: ends - now);
            }

            var run = new Run(Guid.NewGuid(), test, now);
            _active = run;
            _runs.Add(run);
            if (_runs.Count > KeepRuns)
            {
                _runs.RemoveAt(0);
            }

            return new StartResult(StartOutcome.Started, run);
        }
    }

    /// <summary>Frees the lock and starts the cooldown.</summary>
    public void Complete(Run run)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, run))
            {
                return;
            }

            _active = null;
            _cooldownEnds = clock.GetUtcNow() + options.Value.Cooldown;
        }
    }

    public (bool Busy, DateTimeOffset? CooldownEnds, Run? Current) Snapshot()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var cooldown = _cooldownEnds is { } ends && now < ends ? ends : (DateTimeOffset?)null;
            return (_active is not null, cooldown, _runs.Count > 0 ? _runs[^1] : null);
        }
    }

    public Run? Find(Guid id)
    {
        lock (_gate)
        {
            return _runs.FirstOrDefault(r => r.Id == id);
        }
    }
}

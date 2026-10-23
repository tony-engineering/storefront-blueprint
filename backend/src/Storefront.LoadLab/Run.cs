namespace Storefront.LoadLab;

public enum RunPhase
{
    Initializing,
    Sending,
    Finalizing,
    Done,
    Failed,
}

public sealed record Sample(
    int T,
    int Sent,
    int Rps,
    int InFlight,
    double P50,
    double P95,
    int Errors,
    IReadOnlyDictionary<string, int> Replies,
    IReadOnlyDictionary<string, int> Orders,
    int OrdersTotal);

public sealed record AuditCheck(string Id, string Label, string Result, string Detail);

/// <summary>One load-test run. Written by the runner, read by the status endpoint; every access takes the lock.</summary>
public sealed class Run(Guid id, LabTest test, DateTimeOffset startedAt)
{
    private readonly object _gate = new();
    private readonly List<Sample> _samples = [];
    private RunPhase _phase = RunPhase.Initializing;
    private double _percent;
    private DateTimeOffset? _finishedAt;
    private IReadOnlyList<AuditCheck> _audit = [];
    private string? _error;

    public Guid Id { get; } = id;

    public LabTest Test { get; } = test;

    public DateTimeOffset StartedAt { get; } = startedAt;

    public void Update(RunPhase phase, double percent)
    {
        lock (_gate)
        {
            _phase = phase;
            _percent = Math.Clamp(percent, 0, 100);
        }
    }

    public void Progress(double percent)
    {
        lock (_gate)
        {
            _percent = Math.Clamp(percent, 0, 100);
        }
    }

    public void AddSample(Sample sample)
    {
        lock (_gate)
        {
            _samples.Add(sample);
        }
    }

    public void Finish(RunPhase phase, DateTimeOffset now, IReadOnlyList<AuditCheck> audit, string? error = null)
    {
        lock (_gate)
        {
            _phase = phase;
            _percent = phase == RunPhase.Done ? 100 : _percent;
            _finishedAt = now;
            _audit = audit;
            _error = error;
        }
    }

    public RunView View(int after)
    {
        lock (_gate)
        {
            var from = Math.Clamp(after, 0, _samples.Count);
            return new RunView(
                Id, Test.Id, _phase.ToString(), Math.Round(_percent, 1), StartedAt, _finishedAt,
                _audit, _error, _samples.Count, _samples.Skip(from).ToList());
        }
    }
}

public sealed record RunView(
    Guid Id,
    string TestId,
    string Phase,
    double Percent,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<AuditCheck> Audit,
    string? Error,
    int SampleCount,
    IReadOnlyList<Sample> Samples);

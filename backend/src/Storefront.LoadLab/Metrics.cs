using System.Collections.Concurrent;

namespace Storefront.LoadLab;

/// <summary>What one run has seen so far. Written by many request tasks, read once a second by the sampler.</summary>
public sealed class Metrics
{
    private readonly ConcurrentQueue<double> _latenciesMs = new();
    private readonly ConcurrentDictionary<string, int> _replies = new();
    private int _sent;
    private int _inFlight;
    private int _errors;
    private int _gaveUp;

    public int Sent => Volatile.Read(ref _sent);

    public int InFlight => Volatile.Read(ref _inFlight);

    public int Errors => Volatile.Read(ref _errors);

    /// <summary>Requests that ran out of retries without a definitive answer.</summary>
    public int GaveUp => Volatile.Read(ref _gaveUp);

    public int Count5xx => _replies.Where(r => r.Key.StartsWith('5')).Sum(r => r.Value);

    public IReadOnlyDictionary<string, int> Replies => new Dictionary<string, int>(_replies);

    public void Started()
    {
        Interlocked.Increment(ref _sent);
        Interlocked.Increment(ref _inFlight);
    }

    /// <summary>One HTTP answer, a retry after 409 included.</summary>
    public void Reply(string code, double latencyMs)
    {
        _replies.AddOrUpdate(code, 1, (_, n) => n + 1);
        _latenciesMs.Enqueue(latencyMs);
    }

    /// <summary>An attempt that got no HTTP answer at all (connection refused, timeout). It is retried.</summary>
    public void Error() => Interlocked.Increment(ref _errors);

    public void GiveUp() => Interlocked.Increment(ref _gaveUp);

    /// <summary>The request is over, whatever came of it.</summary>
    public void Done() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Upper edges (ms) of the latency histogram; the extra top bucket catches everything slower than the last.</summary>
    public static readonly double[] LatencyBoundsMs = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    /// <summary>
    /// Latency of the replies since the last call: how many, the p50/p95, and a histogram with one count per
    /// <see cref="LatencyBoundsMs"/> bucket plus a final overflow bucket (so <c>LatencyBoundsMs.Length + 1</c> counts).
    /// </summary>
    public (int Completed, double P50, double P95, int[] Buckets) Drain()
    {
        var values = new List<double>();
        while (_latenciesMs.TryDequeue(out var v))
        {
            values.Add(v);
        }

        values.Sort();

        var buckets = new int[LatencyBoundsMs.Length + 1];
        foreach (var v in values)
        {
            var bucket = 0;
            while (bucket < LatencyBoundsMs.Length && v >= LatencyBoundsMs[bucket])
            {
                bucket++;
            }

            buckets[bucket]++;
        }

        return (values.Count, Percentile(values, 0.50), Percentile(values, 0.95), buckets);
    }

    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Count - 1)], 1);
    }
}

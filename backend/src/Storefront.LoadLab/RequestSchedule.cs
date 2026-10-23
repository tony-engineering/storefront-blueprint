namespace Storefront.LoadLab;

/// <summary>When requests are due: how many should have been started after a given time.</summary>
public abstract record RequestSchedule
{
    public abstract int Total { get; }

    /// <summary>How many requests should have started after <paramref name="elapsed"/>; never more than <see cref="Total"/>.</summary>
    public abstract int DueAt(TimeSpan elapsed);

    /// <summary>Everything at once.</summary>
    public sealed record Burst(int Count) : RequestSchedule
    {
        public override int Total => Count;

        public override int DueAt(TimeSpan elapsed) => Count;
    }

    /// <summary>A steady <paramref name="PerSecond"/> for <paramref name="Duration"/>.</summary>
    public sealed record Constant(double PerSecond, TimeSpan Duration) : RequestSchedule
    {
        public override int Total => (int)Math.Round(PerSecond * Duration.TotalSeconds);

        public override int DueAt(TimeSpan elapsed) =>
            Math.Min(Total, (int)Math.Floor(PerSecond * Math.Max(0, elapsed.TotalSeconds) + 1e-9));
    }

    /// <summary>A rate that climbs in a straight line from <paramref name="From"/> to <paramref name="To"/> per second.</summary>
    public sealed record Ramp(double From, double To, TimeSpan Duration) : RequestSchedule
    {
        public override int Total => (int)Math.Round((From + To) / 2 * Duration.TotalSeconds);

        public override int DueAt(TimeSpan elapsed)
        {
            var t = Math.Clamp(elapsed.TotalSeconds, 0, Duration.TotalSeconds);
            var d = Duration.TotalSeconds;
            var due = From * t + (To - From) / (2 * d) * t * t;
            return Math.Min(Total, (int)Math.Floor(due + 1e-9));
        }
    }
}

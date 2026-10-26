namespace Storefront.LoadLab;

/// <summary>One slice of the traffic: this share of orders pays with this token.</summary>
public sealed record TokenShare(string Token, int Percent, int DelayMinMs, int DelayMaxMs)
{
    /// <summary>The token as the API's mock gateway reads it, with the simulated delay.</summary>
    public string Render() =>
        DelayMaxMs <= 0 ? Token
        : DelayMinMs == DelayMaxMs ? $"{Token}@{DelayMinMs}"
        : $"{Token}@{DelayMinMs}-{DelayMaxMs}";

    /// <summary>The mock gateway takes the money for these before it answers.</summary>
    public bool Charges => Token is "tok_success" or "tok_timeout" or "tok_flaky";
}

/// <param name="Keys">Distinct idempotency keys; null means one per request. Fewer keys than requests means repeats.</param>
public sealed record LabTest(
    string Id,
    string Title,
    string Context,
    RequestSchedule Schedule,
    IReadOnlyList<TokenShare> Mix,
    string Shape,
    string MixText,
    string DelayText,
    string Expected,
    int? Keys = null)
{
    public int Requests => Schedule.Total;

    public int KeyCount => Keys ?? Requests;

    /// <summary>The token for request number <paramref name="keyIndex"/>. Deterministic, spread evenly across the mix.</summary>
    public TokenShare ShareFor(int keyIndex)
    {
        var slot = keyIndex * 37 % 100; // 37 is coprime with 100, so the slots interleave
        var upTo = 0;
        foreach (var share in Mix)
        {
            upTo += share.Percent;
            if (slot < upTo)
            {
                return share;
            }
        }

        return Mix[^1];
    }
}

public static class LabTests
{
    private static TimeSpan S(int seconds) => TimeSpan.FromSeconds(seconds);

    public static readonly IReadOnlyList<LabTest> All =
    [
        new("steady", "Busy Saturday", "A normal busy day: a steady stream of orders.",
            new RequestSchedule.Constant(10, S(60)),
            [new("tok_success", 85, 200, 800), new("tok_decline", 10, 200, 800), new("tok_timeout", 5, 0, 0)],
            "10 orders/s for 60 s", "85% ok · 10% decline · 5% timeout", "200–800 ms",
            "Mostly Paid; Unknown turns into Paid."),

        new("flash-sale", "Flash sale", "Everyone clicks at once.",
            new RequestSchedule.Burst(300),
            [new("tok_success", 100, 300, 600)],
            "300 orders at once", "100% ok", "300–600 ms",
            "p95 jumps, all Paid, no 5xx."),

        new("ramp", "Find the limit", "Traffic climbs until something gives.",
            new RequestSchedule.Ramp(2, 40, S(120)),
            [new("tok_success", 90, 200, 800), new("tok_decline", 10, 200, 800)],
            "2 → 40 orders/s over 2 min", "90% ok · 10% decline", "200–800 ms",
            "Shows where latency bends. Slows real shoppers while it runs."),

        new("outage", "Provider down", "The payment provider stops answering.",
            new RequestSchedule.Constant(5, S(40)),
            [new("tok_unavailable", 100, 50, 50)],
            "5 orders/s for 40 s", "100% unavailable", "50 ms",
            "Retrying, then Unavailable at about 62 s. Nobody is charged."),

        new("slow-provider", "Slow provider", "The provider gets slow, slower than our 3 s timeout.",
            new RequestSchedule.Constant(5, S(60)),
            [new("tok_success", 100, 2000, 4000)],
            "5 orders/s for 60 s", "100% ok", "2–4 s",
            "About half Unknown, then Paid."),

        new("restart-api", "Sending requests over 60 seconds", "Restart the API by hand while this runs (docker compose restart api).",
            new RequestSchedule.Constant(10, S(60)),
            [new("tok_success", 100, 200, 800)],
            "10 orders/s for 60 s", "100% ok", "200–800 ms",
            "Every order ends Paid, even though the API is down for a few seconds. Failed calls are retried with the same key."),

        new("restart-db", "Database restart", "Restart the database by hand while this runs (docker compose restart db; on the cluster, delete the storefront-db pod).",
            new RequestSchedule.Constant(10, S(60)),
            [new("tok_success", 100, 200, 800)],
            "10 orders/s for 60 s", "100% ok", "200–800 ms",
            "Every order ends Paid, even though the database is down for a few seconds. Writes fail while it is gone and are retried with the same key; the reconciler finishes any order left mid-payment."),

        new("retry-storm", "Double-clickers", "Every order is sent 20 times with the same key.",
            new RequestSchedule.Burst(400),
            [new("tok_success", 100, 500, 500)],
            "400 requests, 20 keys, at once", "100% ok", "500 ms",
            "Exactly 20 orders.", Keys: 20),

        // No payment delay, so every request is pure API and database work with nothing simulated. A sustained rate
        // well above where the ramp already bends drives more concurrent orders than the single Postgres instance and
        // its ~100-connection pool can clear, so requests queue for a connection and some time out.
        new("pool-storm", "Overload the pool", "More orders at once than the one database instance and its connection pool can serve.",
            new RequestSchedule.Constant(150, S(45)),
            [new("tok_success", 100, 0, 0)],
            "150 orders/s for 45 s", "100% ok", "none",
            "The connection pool saturates: p95 jumps, in-flight climbs, some orders wait or get a 5xx and are retried with the same key. Every order still ends Paid. Shows the single Postgres instance is the ceiling. Slows real shoppers while it runs."),
    ];

    public static LabTest? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Storefront.LoadLab;

/// <summary>What the runner knows about one idempotency key.</summary>
internal sealed class KeyState(Guid key, TokenShare share)
{
    public Guid Key { get; } = key;

    public TokenShare Share { get; } = share;

    private readonly HashSet<Guid> _orderIds = [];

    public int OrderCount
    {
        get
        {
            lock (_orderIds)
            {
                return _orderIds.Count;
            }
        }
    }

    public void Accepted(Guid orderId)
    {
        lock (_orderIds)
        {
            _orderIds.Add(orderId);
        }
    }
}

public sealed record OrderStats(int Total, Dictionary<string, int> ByStatus)
{
    public int Count(string status) => ByStatus.GetValueOrDefault(status);

    /// <summary>Orders whose payment is still being resolved.</summary>
    public int Unsettled => Count("Pending") + Count("PaymentRetrying") + Count("PaymentUnknown");
}

/// <summary>
/// Runs one test in three phases: Initializing (is the API up, which products exist), Sending (the schedule,
/// without waiting for replies) and Finalizing (wait for every payment to settle, then audit).
/// </summary>
public sealed class LoadRunner(
    IHttpClientFactory httpFactory,
    IOptions<LoadLabOptions> options,
    TimeProvider clock,
    ILogger<LoadRunner> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(1);

    public async Task RunAsync(Run run, CancellationToken cancellationToken)
    {
        var test = run.Test;
        var metrics = new Metrics();
        var http = httpFactory.CreateClient("api");
        OrderStats? latest = null;
        using var samplerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sampler = Task.CompletedTask;

        try
        {
            run.Update(RunPhase.Initializing, 0);
            var products = await InitializeAsync(http, cancellationToken);

            var keys = Enumerable.Range(0, test.KeyCount)
                .Select(i => new KeyState(Guid.NewGuid(), test.ShareFor(i)))
                .ToArray();

            var started = clock.GetTimestamp();
            sampler = SampleAsync(run, http, metrics, () => latest, stats => latest = stats, started, samplerStop.Token);

            // Phase 2: Sending
            run.Update(RunPhase.Sending, 0);
            var requests = new List<Task>(test.Requests);
            var next = 0;
            while (next < test.Requests)
            {
                var due = test.Schedule.DueAt(clock.GetElapsedTime(started));
                while (next < due)
                {
                    var keyIndex = next % keys.Length;
                    metrics.Started();
                    requests.Add(SendOrderAsync(http, run.Id, keys[keyIndex], products[keyIndex % products.Count], metrics, cancellationToken));
                    next++;
                }

                run.Progress(100.0 * next / test.Requests);
                if (next < test.Requests)
                {
                    await Task.Delay(Tick, clock, cancellationToken);
                }
            }

            // Phase 3: Finalizing
            run.Update(RunPhase.Finalizing, 0);
            await Task.WhenAll(requests);
            var created = keys.Count(k => k.OrderCount > 0);
            await WaitUntilSettledAsync(run, http, () => latest, created, cancellationToken);

            var final = await GetStatsAsync(http, run.Id, cancellationToken) ?? latest ?? new OrderStats(0, []);
            latest = final;
            var audit = Audit(keys, final, metrics);

            await samplerStop.CancelAsync();
            await sampler;
            run.AddSample(TakeSample(run, metrics, final, started));
            run.Finish(RunPhase.Done, clock.GetUtcNow(), audit);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} of {Test} failed.", run.Id, test.Id);
            await samplerStop.CancelAsync();
            try
            {
                await sampler;
            }
            catch (OperationCanceledException)
            {
            }

            run.Finish(RunPhase.Failed, clock.GetUtcNow(), [], ex is OperationCanceledException ? "The lab is shutting down." : ex.Message);
        }
    }

    private async Task<IReadOnlyList<Guid>> InitializeAsync(HttpClient http, CancellationToken ct)
    {
        using var health = await http.GetAsync("/health", ct);
        health.EnsureSuccessStatusCode();

        var products = await http.GetFromJsonAsync<List<ProductRef>>("/api/products", Json, ct);
        if (products is not { Count: > 0 })
        {
            throw new InvalidOperationException("The store has no products to order.");
        }

        return products.Select(p => p.Id).ToList();
    }

    /// <summary>
    /// Sends one order like a browser would: if there is no answer or the server fails (5xx), or the same key is
    /// still in progress (409), ask again with the same key after a short wait, until <see cref="LoadLabOptions.RetryFor"/>
    /// has passed. The key makes every repeat safe.
    /// </summary>
    private async Task SendOrderAsync(
        HttpClient http, Guid runId, KeyState key, Guid productId, Metrics metrics, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new { lines = new[] { new { productId, quantity = 1 } }, paymentToken = key.Share.Render() }, Json);
        var begun = clock.GetTimestamp();
        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Idempotency-Key", key.Key.ToString());
                request.Headers.Add("X-Lab-Run", runId.ToString());

                var timestamp = Stopwatch.GetTimestamp();
                TimeSpan wait;
                try
                {
                    using var response = await http.SendAsync(request, ct);
                    metrics.Reply(((int)response.StatusCode).ToString(), Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

                    if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Accepted)
                    {
                        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                        key.Accepted(doc.RootElement.GetProperty("id").GetGuid());
                        return;
                    }

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        wait = RetryPause;
                    }
                    else
                    {
                        return; // a definitive answer, such as a 4xx
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    metrics.Error();
                    wait = RetryPause;
                }

                if (clock.GetElapsedTime(begun) + wait > options.Value.RetryFor)
                {
                    metrics.GiveUp();
                    return;
                }

                await Task.Delay(wait, clock, ct);
            }
        }
        finally
        {
            metrics.Done();
        }
    }

    private async Task SampleAsync(
        Run run, HttpClient http, Metrics metrics, Func<OrderStats?> latest, Action<OrderStats?> setLatest,
        long started, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        // The stats call goes to the API under test, so it can hang when the API is saturated. It runs on its own,
        // one at a time, and never holds up the one-second beat of the chart.
        Task refresh = Task.CompletedTask;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (refresh.IsCompleted)
                {
                    refresh = RefreshStatsAsync();
                }

                run.AddSample(TakeSample(run, metrics, latest(), started));
            }
        }
        catch (OperationCanceledException)
        {
        }

        async Task RefreshStatsAsync()
        {
            try
            {
                if (await GetStatsAsync(http, run.Id, ct) is { } stats)
                {
                    setLatest(stats);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private Sample TakeSample(Run run, Metrics metrics, OrderStats? stats, long started)
    {
        var (completed, p50, p95) = metrics.Drain();
        return new Sample(
            T: (int)Math.Round(clock.GetElapsedTime(started).TotalSeconds),
            Sent: metrics.Sent,
            Rps: completed,
            InFlight: metrics.InFlight,
            P50: p50,
            P95: p95,
            Errors: metrics.Errors,
            Replies: metrics.Replies,
            Orders: stats?.ByStatus ?? [],
            OrdersTotal: stats?.Total ?? 0);
    }

    private static async Task<OrderStats?> GetStatsAsync(HttpClient http, Guid runId, CancellationToken ct)
    {
        try
        {
            var stats = await http.GetFromJsonAsync<StatsDto>($"/api/admin/lab/runs/{runId}/order-stats", Json, ct);
            return stats is null ? null : new OrderStats(stats.Total, new Dictionary<string, int>(stats.ByStatus));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Progress is settled ÷ created. Gives up after <see cref="LoadLabOptions.SettleTimeout"/>; the audit then says so.</summary>
    private async Task WaitUntilSettledAsync(
        Run run, HttpClient http, Func<OrderStats?> latest, int created, CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        while (clock.GetElapsedTime(started) < options.Value.SettleTimeout)
        {
            var stats = latest();
            if (stats is not null)
            {
                run.Progress(created == 0 ? 100 : 100.0 * Math.Max(0, stats.Total - stats.Unsettled) / created);
                if (stats.Total >= created && stats.Unsettled == 0)
                {
                    run.Progress(100);
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        }
    }

    internal static IReadOnlyList<AuditCheck> Audit(IReadOnlyList<KeyState> keys, OrderStats stats, Metrics metrics)
    {
        var accepted = keys.Count(k => k.OrderCount > 0);
        var split = keys.Count(k => k.OrderCount > 1);
        var shouldBePaid = keys.Count(k => k.OrderCount > 0 && k.Share.Charges);
        var retried = metrics.Count5xx + metrics.Errors;

        static AuditCheck Check(string id, string label, bool pass, string detail) =>
            new(id, label, pass ? "pass" : "fail", detail);

        return
        [
            Check("created", "Orders created match the unique keys that got 201 or 202",
                stats.Total == accepted, $"{stats.Total} orders · {accepted} keys accepted"),
            Check("one-per-key", "One order ID per key",
                split == 0, split == 0 ? $"{accepted} keys, one order each" : $"{split} keys got more than one order"),
            Check("settled", "No order left Pending, Retrying or Unknown",
                stats.Unsettled == 0, stats.Unsettled == 0 ? "All settled" : $"{stats.Unsettled} still unsettled"),
            Check("paid", "Successful payments ended Paid",
                stats.Count("Paid") == shouldBePaid, $"{stats.Count("Paid")} Paid · {shouldBePaid} expected"),
            Check("answered", "Every request got an answer in the end",
                metrics.GaveUp == 0,
                (metrics.GaveUp == 0 ? "None gave up" : $"{metrics.GaveUp} gave up after retrying")
                + (retried == 0 ? "" : $" · {retried} failed attempts were retried")),
        ];
    }

    private sealed record ProductRef(Guid Id);

    private sealed record StatsDto(int Total, Dictionary<string, int> ByStatus);
}

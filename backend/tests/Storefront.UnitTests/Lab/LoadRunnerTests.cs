using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storefront.LoadLab;
using Xunit;

namespace Storefront.UnitTests.Lab;

/// <summary>Drives the runner against a stand-in API: the phases, the retries like a browser's, and an audit that can pass and fail.</summary>
public sealed class LoadRunnerTests
{
    private sealed class FakeApi(
        bool dropOrders = false, int failFirstOrders = 0, bool alwaysFail = false, bool refuseConnection = false)
        : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<string, Guid> _ordersByKey = [];
        private readonly object _gate = new();
        private int _orderCalls;

        public int Orders
        {
            get
            {
                lock (_gate)
                {
                    return _ordersByKey.Count;
                }
            }
        }

        public HttpClient CreateClient(string name) => new(this) { BaseAddress = new Uri("http://api") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/health")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            if (path == "/api/products")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""[{"id":"{{Guid.NewGuid()}}"}]"""));
            }

            if (path.EndsWith("/order-stats"))
            {
                lock (_gate)
                {
                    var total = dropOrders ? Math.Max(0, _ordersByKey.Count - 1) : _ordersByKey.Count;
                    return Task.FromResult(Json(HttpStatusCode.OK, $"{{\"total\":{total},\"byStatus\":{{\"Paid\":{total}}}}}"));
                }
            }

            // The API is "restarting": the first calls fail, then it is back.
            if (alwaysFail || Interlocked.Increment(ref _orderCalls) <= failFirstOrders)
            {
                return refuseConnection
                    ? throw new HttpRequestException("connection refused")
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var key = request.Headers.GetValues("Idempotency-Key").Single();
            Guid id;
            lock (_gate)
            {
                if (!_ordersByKey.TryGetValue(key, out id))
                {
                    _ordersByKey[key] = id = Guid.NewGuid();
                }
            }

            return Task.FromResult(Json(HttpStatusCode.Created, $$"""{"id":"{{id}}","status":"Paid"}"""));
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Unreachable : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Throwing()) { BaseAddress = new Uri("http://api") };

        private sealed class Throwing : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("connection refused");
        }
    }

    private static LoadRunner Runner(IHttpClientFactory api, TimeSpan? retryFor = null) =>
        new(
            api,
            Options.Create(new LoadLabOptions
            {
                SettleTimeout = TimeSpan.FromSeconds(3),
                RetryFor = retryFor ?? TimeSpan.FromSeconds(30),
            }),
            TimeProvider.System,
            NullLogger<LoadRunner>.Instance);

    private static Run NewRun(string testId) => new(Guid.NewGuid(), LabTests.Find(testId)!, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_clean_run_goes_through_the_phases_and_passes_the_audit()
    {
        var api = new FakeApi();
        var run = NewRun("retry-storm");
        Assert.Equal("Initializing", run.View(0).Phase);

        await Runner(api).RunAsync(run, CancellationToken.None);

        var view = run.View(0);
        Assert.Equal("Done", view.Phase);
        Assert.Equal(100, view.Percent);
        Assert.Equal(20, api.Orders); // 400 requests, 20 keys
        Assert.All(view.Audit, check => Assert.Equal("pass", check.Result));
        Assert.Equal(5, view.Audit.Count);
        Assert.NotEmpty(view.Samples);
    }

    [Fact]
    public async Task A_lost_order_fails_the_audit()
    {
        var run = NewRun("retry-storm");

        await Runner(new FakeApi(dropOrders: true)).RunAsync(run, CancellationToken.None);

        var view = run.View(0);
        Assert.Equal("Done", view.Phase);
        Assert.Equal("fail", view.Audit.Single(c => c.Id == "created").Result);
    }

    [Theory]
    [InlineData(false)] // the API answers 503
    [InlineData(true)] // the API refuses the connection
    public async Task Failed_calls_are_retried_with_the_same_key_until_the_api_is_back(bool refuseConnection)
    {
        var api = new FakeApi(failFirstOrders: 60, refuseConnection: refuseConnection);
        var run = NewRun("retry-storm");

        await Runner(api).RunAsync(run, CancellationToken.None);

        var view = run.View(0);
        Assert.Equal("Done", view.Phase);
        Assert.Equal(20, api.Orders); // one order per key, none lost, none doubled
        Assert.All(view.Audit, check => Assert.Equal("pass", check.Result));
        Assert.Contains("retried", view.Audit.Single(c => c.Id == "answered").Detail);
    }

    [Fact]
    public async Task A_request_that_never_gets_an_answer_gives_up_and_the_audit_says_so()
    {
        var run = NewRun("retry-storm");

        await Runner(new FakeApi(alwaysFail: true), retryFor: TimeSpan.FromSeconds(2)).RunAsync(run, CancellationToken.None);

        var view = run.View(0);
        Assert.Equal("Done", view.Phase);
        Assert.Equal("fail", view.Audit.Single(c => c.Id == "answered").Result);
    }

    [Fact]
    public async Task An_unreachable_api_fails_the_run_in_the_first_phase()
    {
        var run = NewRun("steady");

        await Runner(new Unreachable()).RunAsync(run, CancellationToken.None);

        Assert.Equal("Failed", run.View(0).Phase);
        Assert.NotNull(run.View(0).Error);
    }
}

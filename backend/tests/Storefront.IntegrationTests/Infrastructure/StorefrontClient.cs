using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Storefront.IntegrationTests.Infrastructure;

/// <summary>Thin typed client over the public HTTP API, as the Angular app would call it.</summary>
public sealed class StorefrontClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<IReadOnlyList<ProductDto>>> GetProductsAsync(CancellationToken ct) =>
        await SendAsync<IReadOnlyList<ProductDto>>(new HttpRequestMessage(HttpMethod.Get, "/api/products"), ct);

    public async Task<ApiResponse<OrderDto>> GetOrderAsync(Guid orderId, CancellationToken ct) =>
        await SendAsync<OrderDto>(new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}"), ct);

    /// <summary>POST /api/orders with a fresh Idempotency-Key unless one is given.</summary>
    public async Task<ApiResponse<OrderDto>> PlaceOrderAsync(
        CreateOrderRequest request, CancellationToken ct, Guid? idempotencyKey = null, Guid? labRun = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        message.Headers.Add("Idempotency-Key", (idempotencyKey ?? Guid.NewGuid()).ToString());
        if (labRun is not null)
        {
            message.Headers.Add("X-Lab-Run", labRun.Value.ToString());
        }

        return await SendAsync<OrderDto>(message, ct);
    }

    public async Task<ApiResponse<LabOrderStats>> GetLabStatsAsync(Guid runId, CancellationToken ct) =>
        await SendAsync<LabOrderStats>(new HttpRequestMessage(HttpMethod.Get, $"/api/admin/lab/runs/{runId}/order-stats"), ct);

    /// <summary>POST /api/orders/{id}/payments: pay an existing order again, e.g. with another card.</summary>
    public async Task<ApiResponse<OrderDto>> RetryPaymentAsync(
        Guid orderId, string paymentToken, CancellationToken ct, Guid? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments")
        {
            Content = JsonContent.Create(new RetryPaymentRequest(paymentToken), options: Json),
        };
        message.Headers.Add("Idempotency-Key", (idempotencyKey ?? Guid.NewGuid()).ToString());
        return await SendAsync<OrderDto>(message, ct);
    }

    /// <summary>POST /api/orders with a hand-written body and a raw header value (or none), to test bad input.</summary>
    public async Task<ApiResponse<OrderDto>> PlaceOrderJsonAsync(string json, string? idempotencyKey, CancellationToken ct)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await SendAsync<OrderDto>(message, ct);
    }

    public async Task<ApiResponse<ProductDto>> GetProductAsync(Guid id, CancellationToken ct) =>
        await SendAsync<ProductDto>(new HttpRequestMessage(HttpMethod.Get, $"/api/products/{id}"), ct);

    public async Task<ApiResponse<ProductDto>> CreateProductAsync(CreateProductRequest request, CancellationToken ct) =>
        await SendAsync<ProductDto>(
            new HttpRequestMessage(HttpMethod.Post, "/api/admin/products") { Content = JsonContent.Create(request, options: Json) },
            ct);

    public async Task<ApiResponse<object>> DeleteProductAsync(Guid id, CancellationToken ct) =>
        await SendAsync<object>(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/products/{id}"), ct);

    private async Task<ApiResponse<T>> SendAsync<T>(HttpRequestMessage message, CancellationToken ct)
    {
        using var response = await http.SendAsync(message, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var body = response.IsSuccessStatusCode && raw.Length > 0
            ? JsonSerializer.Deserialize<T>(raw, Json)
            : default;
        return new ApiResponse<T>(response.StatusCode, response.Headers, body, raw);
    }
}

public sealed record ApiResponse<T>(HttpStatusCode StatusCode, HttpResponseHeaders Headers, T? Body, string RawBody)
{
    /// <summary>Fails with the response body in the message, so a ProblemDetails error is readable.</summary>
    public ApiResponse<T> ShouldHaveStatus(HttpStatusCode expected)
    {
        if (StatusCode != expected)
        {
            Assert.Fail($"Expected HTTP {(int)expected} {expected} but got {(int)StatusCode} {StatusCode}. Body: {RawBody}");
        }

        return this;
    }

    private static readonly JsonSerializerOptions ProblemJson = new(JsonSerializerDefaults.Web);

    /// <summary>The error body of a failed response.</summary>
    public ProblemDto RequiredProblem =>
        string.IsNullOrWhiteSpace(RawBody)
            ? throw new InvalidOperationException($"Response had no error body (HTTP {(int)StatusCode}).")
            : JsonSerializer.Deserialize<ProblemDto>(RawBody, ProblemJson)!;

    public bool WasReplayed => Headers.TryGetValues("Idempotent-Replayed", out var values) && values.Contains("true");

    public T RequiredBody => Body ?? throw new InvalidOperationException($"Response had no body. Raw: {RawBody}");
}

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Storefront.Api.Features;

namespace Storefront.Api.Idempotency;

/// <summary>
/// A finished HTTP response, kept as text so a repeat of the same <c>Idempotency-Key</c> gets exactly the same
/// status, body and <c>Location</c> back (ARCHITECTURE.md §6).
/// </summary>
public sealed record StoredResponse(int Status, string Body, string? Location = null)
{
    public string ContentType => Status >= 400 ? "application/problem+json" : "application/json";

    public static StoredResponse Json<T>(int status, T value, JsonSerializerOptions options, string? location = null) =>
        new(status, JsonSerializer.Serialize(value, options), location);

    public static StoredResponse FromProblem(ProblemDetails problem, JsonSerializerOptions options) =>
        new(problem.Status ?? StatusCodes.Status500InternalServerError, JsonSerializer.Serialize(problem.WithTrace(), options));
}

/// <summary>Writes a <see cref="StoredResponse"/>, flagging replays with <c>Idempotent-Replayed: true</c>.</summary>
internal sealed class StoredResponseResult(StoredResponse response, bool replayed) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var http = httpContext.Response;
        http.StatusCode = response.Status;
        http.ContentType = response.ContentType;

        if (response.Location is not null)
        {
            http.Headers.Location = response.Location;
        }

        if (replayed)
        {
            http.Headers["Idempotent-Replayed"] = "true";
        }

        await http.WriteAsync(response.Body, httpContext.RequestAborted);
    }
}

/// <summary>Adds one response header to another result (e.g. <c>Retry-After</c> to a 409).</summary>
internal sealed class HeaderResult(IResult inner, string name, string value) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers[name] = value;
        return inner.ExecuteAsync(httpContext);
    }
}

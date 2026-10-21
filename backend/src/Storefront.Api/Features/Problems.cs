using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace Storefront.Api.Features;

/// <summary>
/// The one place errors are shaped (ARCHITECTURE.md §10): ProblemDetails (RFC 9457) with a stable
/// <c>type</c> URI such as <c>/errors/idempotency-key-reused</c>, a readable <c>detail</c> and a <c>traceId</c>.
/// </summary>
internal static class Problems
{
    public static ProblemDetails Create(int status, string slug, string title, string detail) => new()
    {
        Status = status,
        Type = $"/errors/{slug}",
        Title = title,
        Detail = detail,
    };

    public static IResult Result(ProblemDetails problem) => TypedResults.Problem(problem);

    public static IResult Result(int status, string slug, string title, string detail) =>
        Result(Create(status, slug, title, detail));

    public static IResult NotFound(string what, Guid id) =>
        Result(StatusCodes.Status404NotFound, "not-found", "Not found", $"No {what} with ID {id} exists.");

    /// <summary>A 400 listing every broken rule per field, e.g. <c>lines[0].quantity</c>.</summary>
    public static IResult Validation(ValidationResult validation) =>
        TypedResults.ValidationProblem(
            validation.Errors
                .GroupBy(e => CamelCase(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()),
            type: "/errors/validation",
            title: "The request is not valid.");

    /// <summary>Runs the validator; returns the 400 to send, or null when the request is fine.</summary>
    public static async Task<IResult?> ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid ? null : Validation(result);
    }

    /// <summary>Adds the trace ID the way the framework does for responses it writes itself.</summary>
    public static ProblemDetails WithTrace(this ProblemDetails problem)
    {
        problem.Extensions["traceId"] = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        return problem;
    }

    /// <summary><c>Lines[0].Quantity</c> → <c>lines[0].quantity</c>, matching the JSON the client sent.</summary>
    private static string CamelCase(string path) =>
        string.Join('.', path.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}

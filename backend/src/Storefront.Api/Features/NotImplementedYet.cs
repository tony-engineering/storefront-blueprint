namespace Storefront.Api.Features;

/// <summary>Placeholder response for routes whose logic is not written yet.</summary>
internal static class NotImplementedYet
{
    public static IResult Problem(string operation) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not implemented",
            detail: $"{operation} is not implemented yet.");
}

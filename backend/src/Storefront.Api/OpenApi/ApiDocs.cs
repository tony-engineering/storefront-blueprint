using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Storefront.Api.OpenApi;

/// <summary>
/// OpenAPI document (<c>/openapi/v1.json</c>) and Swagger UI (<c>/swagger</c>).
/// Both are public: no login is needed to read the docs or send requests from them.
/// Admin endpoints still check the bearer token sent with each request.
/// </summary>
public static class ApiDocs
{
    public const string DocumentName = "v1";
    public const string DocumentPath = $"/openapi/{DocumentName}.json";
    public const string UiPath = "swagger";
    public const string BearerSchemeId = "Bearer";

    public static IServiceCollection AddApiDocs(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(DescribeDocument);
            options.AddOperationTransformer(MarkSecuredOperations);
        });

        return services;
    }

    /// <summary>
    /// Maps the docs unless <c>ApiDocs:Enabled</c> is false. On by default in every environment,
    /// so the docs work under docker compose and in the integration tests alike.
    /// </summary>
    public static WebApplication MapApiDocs(this WebApplication app)
    {
        if (!app.Configuration.GetValue("ApiDocs:Enabled", defaultValue: true))
        {
            return app;
        }

        // AllowAnonymous keeps the docs public once a fallback authorization policy is added.
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = UiPath;
            ui.SwaggerEndpoint(DocumentPath, "Storefront API v1");
            ui.DocumentTitle = "Storefront API";
            ui.EnableTryItOutByDefault();      // every operation is ready to send, no extra click
            ui.DisplayRequestDuration();
            ui.EnablePersistAuthorization();   // keeps a pasted admin token across page reloads
        });

        app.MapGet("/", () => Results.Redirect($"/{UiPath}")).ExcludeFromDescription();

        return app;
    }

    private static Task DescribeDocument(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Storefront API",
            Version = DocumentName,
            Description = """
                Browse products, place orders with a mocked payment step, look orders up, and manage the catalog as an admin.

                **Money** is always an integer amount in minor units (pence) plus a currency code.

                **Idempotency.** `POST /api/orders` and `POST /api/orders/{id}/payments` need an `Idempotency-Key` header (a UUID).
                Send the same key to retry safely: the original response is replayed with `Idempotent-Replayed: true`.
                Same key with a different body returns 422; same key while the first request is still running returns 409.

                **Mock payment tokens** (`paymentToken` in the request body):

                | Token | Outcome |
                |---|---|
                | `tok_success` | Charge succeeds |
                | `tok_decline` | Declined (`insufficient_funds`) |
                | `tok_timeout` | Times out; the charge actually went through, so the reconciler later finds it `Paid` |
                | `tok_error` | Provider error; nothing charged (`PaymentFailed`) |
                | `tok_unavailable` | Provider refuses every call; nothing charged (`PaymentUnavailable`) |
                | `tok_flaky` | Status unavailable for two reconciler checks, then `Paid` |

                **Errors** are ProblemDetails (RFC 9457).

                **Admin endpoints** need a bearer token: click **Authorize** and paste it.
                """,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT for the admin endpoints. Paste the token only, without the \"Bearer \" prefix.",
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the bearer requirement (padlock icon in Swagger UI) and the 401/403 responses to every
    /// endpoint that requires authorization, so docs follow <c>RequireAuthorization</c> automatically.
    /// </summary>
    private static Task MarkSecuredOperations(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
        if (!requiresAuth)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid bearer token." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Token is valid but lacks the Admin role." });

        return Task.CompletedTask;
    }
}

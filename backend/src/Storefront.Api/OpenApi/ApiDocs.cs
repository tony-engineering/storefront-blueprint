using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Storefront.Api.OpenApi;

/// <summary>
/// OpenAPI document (<c>/openapi/v1.json</c>) and Swagger UI (<c>/swagger</c>).
/// Both are public: no login is needed to read the docs or send requests from them.
/// </summary>
public static class ApiDocs
{
    public const string DocumentName = "v1";
    public const string DocumentPath = $"/openapi/{DocumentName}.json";
    public const string UiPath = "swagger";

    /// <summary>
    /// Pre-filled into every <c>Idempotency-Key</c> field in Swagger UI. A valid, non-empty UUID, so the field is never
    /// blank. The UI swaps it for a fresh UUID on each send (see <see cref="SwapPlaceholdersScript"/>), so
    /// clicking Execute twice is two separate attempts. Type any other value to test a replay.
    /// </summary>
    public const string IdempotencyKeyPlaceholder = "00000000-0000-4000-8000-000000000000";

    /// <summary>Seeded product ("Burrow") pre-filled into every product <c>{id}</c> field.</summary>
    public const string ProductIdExample = "5eed0000-0000-4000-8000-000000000007";

    /// <summary>Seeded paid order ("DbSeeder.DemoOrderId") pre-filled into every order <c>{id}</c> field.</summary>
    public const string OrderIdExample = "0de20000-0000-4000-8000-000000000001";

    private const string SwapPlaceholdersScript = $$"""
        (req) => {
          if (req.headers && req.headers['Idempotency-Key'] === '{{IdempotencyKeyPlaceholder}}') {
            req.headers['Idempotency-Key'] = crypto.randomUUID();
          }
          return req;
        }
        """;

    public static IServiceCollection AddApiDocs(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(DescribeDocument);
            options.AddOperationTransformer(PrefillExamples);
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

        app.MapOpenApi("/openapi/{documentName}.json");

        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = UiPath;
            ui.SwaggerEndpoint(DocumentPath, "Storefront API v1");
            ui.DocumentTitle = "Storefront API";
            ui.EnableTryItOutByDefault();      // every operation is ready to send, no extra click
            ui.DisplayRequestDuration();
            ui.UseRequestInterceptor(SwapPlaceholdersScript);   // fresh Idempotency-Key per send
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
                """,
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gives header and path parameters an example, which Swagger UI pre-fills: the Idempotency-Key placeholder, the
    /// seeded product for <c>/api/products/{id}</c>, and the seeded order for <c>/api/orders/{id}</c>.
    /// </summary>
    private static Task PrefillExamples(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath ?? string.Empty;

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.In == ParameterLocation.Header
                && string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Example = JsonValue.Create(IdempotencyKeyPlaceholder);
            }
            else if (parameter.In == ParameterLocation.Path && string.Equals(parameter.Name, "id", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("api/products", StringComparison.OrdinalIgnoreCase))
                {
                    parameter.Example = JsonValue.Create(ProductIdExample);
                }
                else if (path.StartsWith("api/orders", StringComparison.OrdinalIgnoreCase))
                {
                    parameter.Example = JsonValue.Create(OrderIdExample);
                }
            }
        }

        return Task.CompletedTask;
    }
}

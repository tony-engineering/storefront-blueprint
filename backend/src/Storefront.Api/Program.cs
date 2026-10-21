using System.Text.Json.Serialization;
using Storefront.Api.Features.Orders;
using Storefront.Api.Features.Products;
using Storefront.Api.OpenApi;
using Storefront.Api.Payments;
using Storefront.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApiDocs();
builder.Services.AddPersistence();
builder.Services.AddPayments();

// Numbers must be JSON numbers: "1999" is rejected for a price. ASP.NET's web defaults accept
// numeric strings, which would also make the OpenAPI schema (and the generated Angular client)
// type every number as `number | string`.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapApiDocs();
app.MapHealthChecks("/health").ExcludeFromDescription();
app.MapProductEndpoints();
app.MapOrderEndpoints();

await app.InitialiseDatabaseAsync();

await app.RunAsync();

// Lets WebApplicationFactory<Program> in the integration tests reach the entry point.
public partial class Program;

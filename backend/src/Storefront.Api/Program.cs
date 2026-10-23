using System.Text.Json.Serialization;
using Storefront.Api.Features.Lab;
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
builder.Services.AddOrders();
builder.Services.AddProducts();
builder.Services.AddLab();

// Numbers must be JSON numbers: "1999" is rejected for a price. ASP.NET's web defaults accept
// numeric strings, which would also make the OpenAPI schema type every number as `number | string`.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;

    // Enums travel as their names ("Paid"), which is what the OpenAPI document and the Angular client expect.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapApiDocs();
app.MapHealthChecks("/health").ExcludeFromDescription();
app.MapProductEndpoints();
app.MapOrderEndpoints();
app.MapLabEndpoints();

// `dotnet Storefront.Api.dll migrate`: the deploy pipeline's migration step (ARCHITECTURE.md §8).
// Applies migrations, seeds if Database:SeedOnStartup is on, and exits without serving requests.
if (args is ["migrate"])
{
    await app.MigrateDatabaseAsync();
    return;
}

await app.InitialiseDatabaseAsync();

await app.RunAsync();

// Lets WebApplicationFactory<Program> in the integration tests reach the entry point.
public partial class Program;

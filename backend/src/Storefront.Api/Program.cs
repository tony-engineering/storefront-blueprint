using Storefront.Api.Features.Orders;
using Storefront.Api.Features.Products;
using Storefront.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddPersistence();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
app.MapProductEndpoints();
app.MapOrderEndpoints();

await app.InitialiseDatabaseAsync();

await app.RunAsync();

// Lets WebApplicationFactory<Program> in the integration tests reach the entry point.
public partial class Program;

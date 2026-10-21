using Storefront.Api.Features.Orders;
using Storefront.Api.Features.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health");
app.MapProductEndpoints();
app.MapOrderEndpoints();

app.Run();

// Lets WebApplicationFactory<Program> in the integration tests reach the entry point.
public partial class Program;

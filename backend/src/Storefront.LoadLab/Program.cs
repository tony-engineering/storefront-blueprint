using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Storefront.LoadLab;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<LoadLabOptions>().BindConfiguration(LoadLabOptions.SectionName);
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddSingleton<LoadRunner>();
builder.Services
    .AddHttpClient("api", (provider, http) =>
    {
        http.BaseAddress = new Uri(provider.GetRequiredService<IOptions<LoadLabOptions>>().Value.ApiBaseUrl);
        http.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxConnectionsPerServer = 500 });

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapLabEndpoints();

app.Run();

public partial class Program;

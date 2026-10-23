using Microsoft.Extensions.Options;

namespace Storefront.LoadLab;

public static class LabEndpoints
{
    public static IEndpointRouteBuilder MapLabEndpoints(this IEndpointRouteBuilder app)
    {
        var lab = app.MapGroup("/api/lab");

        lab.MapGet("/health", () => Results.Ok());

        lab.MapGet("/tests", () => LabTests.All.Select(t => new
        {
            t.Id,
            t.Title,
            t.Context,
            t.Requests,
            t.Shape,
            Mix = t.MixText,
            Delay = t.DelayText,
            t.Expected,
        }));

        lab.MapGet("/status", (RunCoordinator coordinator, IOptions<LoadLabOptions> options, int? after, Guid? run) =>
        {
            var (busy, cooldownEnds, current) = coordinator.Snapshot();
            // A visitor who last saw another run starts again from the first sample.
            var view = current?.View(current.Id == run ? after ?? 0 : 0);
            return Results.Ok(new { options.Value.Enabled, Busy = busy, CooldownEndsAt = cooldownEnds, Run = view });
        });

        lab.MapPost("/runs", (StartRequest request, RunCoordinator coordinator, LoadRunner runner, IHostApplicationLifetime lifetime, HttpResponse response) =>
        {
            var result = coordinator.TryStart(request.TestId);
            switch (result.Outcome)
            {
                case StartOutcome.Started:
                    var run = result.Run!;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await runner.RunAsync(run, lifetime.ApplicationStopping);
                        }
                        finally
                        {
                            coordinator.Complete(run);
                        }
                    });
                    return Results.Accepted($"/api/lab/runs/{run.Id}", run.View(0));

                case StartOutcome.UnknownTest:
                    return Results.Problem(statusCode: 404, title: "Unknown test", detail: $"No test with ID '{request.TestId}'.");

                case StartOutcome.CoolingDown:
                    response.Headers.RetryAfter = ((int)Math.Ceiling(result.RetryAfter.TotalSeconds)).ToString();
                    return Results.Problem(statusCode: 429, title: "Cooling down", detail: "Another test just finished. Try again shortly.");

                case StartOutcome.Disabled:
                    return Results.Problem(statusCode: 503, title: "The lab is off");

                default:
                    return Results.Problem(statusCode: 409, title: "A test is already running", detail: "Only one test runs at a time.");
            }
        });

        lab.MapGet("/runs/{id:guid}", (Guid id, RunCoordinator coordinator) =>
            coordinator.Find(id) is { } run ? Results.Ok(run.View(0)) : Results.NotFound());

        return app;
    }
}

public sealed record StartRequest(string TestId);

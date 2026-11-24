using TraceMonitor.Api.Auth;
using TraceMonitor.Api.Data;
using TraceMonitor.Api.Models;

namespace TraceMonitor.Api.Endpoints;

public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ingest").AllowAnonymous();

        group.MapPost("/batch", async (
            HttpRequest httpRequest,
            BatchIngestRequest body,
            ApiKeyValidator apiKeyValidator,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var apiKey = httpRequest.Headers["X-Api-Key"].ToString();
            var appId = await apiKeyValidator.ValidateAsync(apiKey, ct);
            if (!appId.HasValue)
            {
                return Results.Unauthorized();
            }

            if (body.Events is null || body.Events.Count == 0)
            {
                return Results.Ok(new { ingested = 0 });
            }

            if (body.Events.Count > 500)
            {
                return Results.BadRequest(new { error = "Maximum 500 events per batch." });
            }

            var count = await repository.InsertLogBatchAsync(appId.Value, body.Events, ct);
            return Results.Ok(new { ingested = count });
        });

        group.MapPost("/metrics", async (
            HttpRequest httpRequest,
            MetricSnapshotIngestDto body,
            ApiKeyValidator apiKeyValidator,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var apiKey = httpRequest.Headers["X-Api-Key"].ToString();
            var appId = await apiKeyValidator.ValidateAsync(apiKey, ct);
            if (!appId.HasValue)
            {
                return Results.Unauthorized();
            }

            await repository.InsertMetricAsync(appId.Value, body, ct);
            return Results.Ok(new { status = "ok" });
        });
    }
}

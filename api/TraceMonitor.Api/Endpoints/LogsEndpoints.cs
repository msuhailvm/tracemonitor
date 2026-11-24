using TraceMonitor.Api.Data;
using TraceMonitor.Api.Models;

namespace TraceMonitor.Api.Endpoints;

public static class LogsEndpoints
{
    public static void MapLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/logs").RequireAuthorization();

        group.MapGet("/", async (
            string? q,
            string? applicationIds,
            string? levels,
            string? eventTypes,
            string? statusRange,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? traceId,
            double? minElapsedMs,
            int? page,
            int? pageSize,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var req = new LogSearchRequest
            {
                Q = q,
                ApplicationIds = ParseGuids(applicationIds),
                Levels = ParseCsv(levels),
                EventTypes = ParseCsv(eventTypes),
                StatusRange = statusRange,
                From = from,
                To = to,
                TraceId = traceId,
                MinElapsedMs = minElapsedMs,
                Page = page ?? 1,
                PageSize = pageSize ?? 50
            };

            var result = await repository.SearchLogsAsync(req, ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id:long}", async (long id, TraceRepository repository, CancellationToken ct) =>
        {
            var detail = await repository.GetLogByIdAsync(id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapGet("/trace/{traceId}", async (string traceId, TraceRepository repository, CancellationToken ct) =>
        {
            var items = await repository.GetLogsByTraceIdAsync(traceId, ct);
            return Results.Ok(new { traceId, items });
        });
    }

    private static string[]? ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    private static Guid[]? ParseGuids(string? value)
    {
        var parts = ParseCsv(value);
        if (parts is null)
        {
            return null;
        }

        var guids = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (Guid.TryParse(part, out var g))
            {
                guids.Add(g);
            }
        }

        return guids.Count == 0 ? null : guids.ToArray();
    }
}

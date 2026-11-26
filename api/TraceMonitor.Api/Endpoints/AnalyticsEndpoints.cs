using TraceMonitor.Api.Data;
using TraceMonitor.Api.Models;

namespace TraceMonitor.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics").RequireAuthorization();

        group.MapGet("/volume", async (
            string? q,
            string? applicationIds,
            string? levels,
            string? eventTypes,
            string? statusRange,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? traceId,
            double? minElapsedMs,
            string? groupBy,
            string? interval,
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
                MinElapsedMs = minElapsedMs
            };

            var result = await repository.GetVolumeAnalyticsAsync(req, groupBy, interval, ct);
            return Results.Ok(result);
        });

        group.MapGet("/error-rate", async (
            string? applicationIds,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? interval,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var result = await repository.GetErrorRateAnalyticsAsync(
                ParseGuids(applicationIds),
                from,
                to,
                interval,
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/latency", async (
            string? applicationIds,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? interval,
            int? top,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var result = await repository.GetLatencyAnalyticsAsync(
                ParseGuids(applicationIds),
                from,
                to,
                interval,
                top ?? 10,
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/resources", async (
            string? applicationIds,
            DateTimeOffset? from,
            DateTimeOffset? to,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var result = await repository.GetResourceAnalyticsAsync(
                ParseGuids(applicationIds),
                from,
                to,
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/top-exceptions", async (
            string? applicationIds,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? limit,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            var result = await repository.GetTopExceptionsAsync(
                ParseGuids(applicationIds),
                from,
                to,
                limit ?? 15,
                ct);
            return Results.Ok(result);
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

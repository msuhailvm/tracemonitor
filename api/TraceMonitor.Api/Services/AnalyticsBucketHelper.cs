namespace TraceMonitor.Api.Services;

public static class AnalyticsBucketHelper
{
    public static (DateTimeOffset From, DateTimeOffset To) ResolveTimeWindow(
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeSpan? defaultSpan = null)
    {
        var resolvedTo = to ?? DateTimeOffset.UtcNow;
        var resolvedFrom = from ?? resolvedTo.Subtract(defaultSpan ?? TimeSpan.FromHours(24));

        if (resolvedFrom > resolvedTo)
        {
            (resolvedFrom, resolvedTo) = (resolvedTo, resolvedFrom);
        }

        return (resolvedFrom, resolvedTo);
    }

    /// <summary>
    /// Chooses a clean time bucket size (in seconds) that yields roughly 20–90 bars for any time window.
    /// </summary>
    public static int ResolveBucketSeconds(DateTimeOffset from, DateTimeOffset to, string? requestedInterval = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedInterval))
        {
            return requestedInterval.Trim().ToLowerInvariant() switch
            {
                "1m" or "minute" => 60,
                "5m" => 300,
                "15m" => 900,
                "1h" or "hour" => 3600,
                "6h" => 21600,
                "1d" or "day" => 86400,
                _ => ChooseAutoBucketSeconds(to - from)
            };
        }

        return ChooseAutoBucketSeconds(to - from);
    }

    private static int ChooseAutoBucketSeconds(TimeSpan span)
    {
        if (span <= TimeSpan.FromHours(2)) return 60;         // 1m
        if (span <= TimeSpan.FromHours(8)) return 300;        // 5m
        if (span <= TimeSpan.FromHours(24)) return 900;       // 15m
        if (span <= TimeSpan.FromDays(4)) return 3600;        // 1h
        if (span <= TimeSpan.FromDays(14)) return 21600;      // 6h
        return 86400;                                         // 1d
    }

    /// <summary>
    /// Builds a PostgreSQL expression that buckets a TIMESTAMPTZ column into the given bucket size.
    /// Uses date_trunc for standard units and floor(epoch) for multi-unit intervals.
    /// </summary>
    public static string BuildBucketExpression(string columnSql, int bucketSeconds) =>
        bucketSeconds switch
        {
            60 => $"date_trunc('minute', {columnSql})",
            3600 => $"date_trunc('hour', {columnSql})",
            86400 => $"date_trunc('day', {columnSql})",
            _ => $"to_timestamp(floor(extract(epoch from {columnSql}) / {bucketSeconds}) * {bucketSeconds})"
        };

    public static DateTimeOffset FloorToBucket(DateTimeOffset value, int bucketSeconds)
    {
        var unix = value.ToUnixTimeSeconds();
        var floored = (unix / bucketSeconds) * bucketSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(floored);
    }
}

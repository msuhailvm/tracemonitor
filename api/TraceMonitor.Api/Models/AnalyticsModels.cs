namespace TraceMonitor.Api.Models;

public sealed class VolumeBucketDto
{
    public DateTimeOffset Timestamp { get; set; }
    public long Total { get; set; }
    public Dictionary<string, long> Counts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VolumeAnalyticsResponse
{
    public int BucketSeconds { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string GroupBy { get; set; } = "level";
    public long TotalEvents { get; set; }
    public IReadOnlyList<VolumeBucketDto> Buckets { get; set; } = [];
}

public sealed class ErrorRateSummaryDto
{
    public long TotalRequests { get; set; }
    public long TotalEvents { get; set; }
    public long TotalErrors { get; set; }
    public long HttpErrorCount { get; set; }
    public double ErrorRatePercent { get; set; }
}

public sealed class ErrorRateBucketDto
{
    public DateTimeOffset Timestamp { get; set; }
    public long Requests { get; set; }
    public long Errors { get; set; }
    public double ErrorRatePercent { get; set; }
}

public sealed class AppErrorBreakdownDto
{
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public long TotalErrors { get; set; }
    public double ErrorRatePercent { get; set; }
}

public sealed class ErrorRateAnalyticsResponse
{
    public int BucketSeconds { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ErrorRateSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<ErrorRateBucketDto> Timeline { get; set; } = [];
    public IReadOnlyList<AppErrorBreakdownDto> ByApplication { get; set; } = [];
}

public sealed class LatencySummaryDto
{
    public long SampleCount { get; set; }
    public double AvgMs { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
}

public sealed class LatencyBucketDto
{
    public DateTimeOffset Timestamp { get; set; }
    public long Count { get; set; }
    public double AvgMs { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
}

public sealed class SlowEndpointDto
{
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public string? HttpMethod { get; set; }
    public string? TargetHost { get; set; }
    public string RequestPath { get; set; } = string.Empty;
    public long Count { get; set; }
    public long ErrorCount { get; set; }
    public double AvgMs { get; set; }
    public double P95Ms { get; set; }
    public double MaxMs { get; set; }
}

public sealed class LatencyAnalyticsResponse
{
    public int BucketSeconds { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public LatencySummaryDto Summary { get; set; } = new();
    public IReadOnlyList<LatencyBucketDto> Timeline { get; set; } = [];
    public IReadOnlyList<SlowEndpointDto> SlowestEndpoints { get; set; } = [];
    public IReadOnlyList<SlowEndpointDto> SlowestDependencies { get; set; } = [];
}

public sealed class ResourcePointDto
{
    public DateTimeOffset Timestamp { get; set; }
    public double CpuPercent { get; set; }
    public double WorkingSetMb { get; set; }
    public double GcHeapMb { get; set; }
    public int ThreadCount { get; set; }
    public long PendingWorkItems { get; set; }
    public int Gen2Collections { get; set; }
}

public sealed class AppResourceSeriesDto
{
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public IReadOnlyList<ResourcePointDto> Points { get; set; } = [];
}

public sealed class ResourceAnalyticsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<AppResourceSeriesDto> Series { get; set; } = [];
}

public sealed class TopExceptionDto
{
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string? RequestPath { get; set; }
    public string? SampleMessage { get; set; }
    public long Count { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

public sealed class TopExceptionsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<TopExceptionDto> Items { get; set; } = [];
}

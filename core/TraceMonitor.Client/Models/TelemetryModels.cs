namespace TraceMonitor.Client.Models;

public sealed class LogEventDto
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = "Log"; // Request | OutgoingHttp | Log | Exception | Lifecycle
    public string Level { get; set; } = "Information"; // Debug | Information | Warning | Error | Critical
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? Environment { get; set; }

    // HTTP context
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? QueryString { get; set; }
    public string? TargetHost { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }

    // Log / Exception details
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }

    // Structured payloads
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}

public sealed class BatchIngestRequest
{
    public List<LogEventDto> Events { get; set; } = [];
}

public sealed class MetricSnapshotDto
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? MachineName { get; set; }
    public int ProcessId { get; set; }
    public double CpuPercent { get; set; }
    public double WorkingSetMb { get; set; }
    public double GcHeapMb { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public int ThreadCount { get; set; }
    public long PendingWorkItems { get; set; }
}

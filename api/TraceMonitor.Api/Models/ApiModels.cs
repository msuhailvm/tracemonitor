namespace TraceMonitor.Api.Models;

public sealed class UserRow
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class ApplicationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateApplicationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Environment { get; set; } = "Production";
}

public sealed class CreateApplicationResponse
{
    public ApplicationDto Application { get; set; } = new();
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ApplicationHealthDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public double? CpuPercent { get; set; }
    public double? WorkingSetMb { get; set; }
    public double? GcHeapMb { get; set; }
    public int? ThreadCount { get; set; }
    public string? MachineName { get; set; }
    public int? ProcessId { get; set; }
    public string Status { get; set; } = "Unknown"; // Online | Stale | Unknown
}

public sealed class BatchIngestRequest
{
    public List<LogEventIngestDto> Events { get; set; } = [];
}

public sealed class LogEventIngestDto
{
    public DateTimeOffset? Timestamp { get; set; }
    public string? EventType { get; set; }
    public string? Level { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? Environment { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? QueryString { get; set; }
    public string? TargetHost { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}

public sealed class MetricSnapshotIngestDto
{
    public DateTimeOffset? Timestamp { get; set; }
    public string? MachineName { get; set; }
    public int? ProcessId { get; set; }
    public double CpuPercent { get; set; }
    public double WorkingSetMb { get; set; }
    public double GcHeapMb { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public int ThreadCount { get; set; }
    public long PendingWorkItems { get; set; }
}

public sealed class LogSearchRequest
{
    public string? Q { get; set; }
    public Guid[]? ApplicationIds { get; set; }
    public string[]? Levels { get; set; }
    public string[]? EventTypes { get; set; }
    public string? StatusRange { get; set; } // 2xx | 4xx | 5xx | exact number
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? TraceId { get; set; }
    public double? MinElapsedMs { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class LogSummaryDto
{
    public long Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? Environment { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? QueryString { get; set; }
    public string? TargetHost { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
}

public sealed class LogDetailDto
{
    public long Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationSlug { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? Environment { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? QueryString { get; set; }
    public string? TargetHost { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? RequestHeadersJson { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? PropertiesJson { get; set; }
}

public sealed class LogSearchResponse
{
    public IReadOnlyList<LogSummaryDto> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

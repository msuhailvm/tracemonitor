using Microsoft.Extensions.Logging;

namespace TraceMonitor.Client.Configuration;

public sealed class TraceMonitorOptions
{
    public const string SectionName = "TraceMonitor";

    /// <summary>Master switch to enable/disable telemetry shipping.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of TraceMonitor.Api, e.g. https://logs.tracemonitor.online</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Ingestion API key issued for this application.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional display name or environment override.</summary>
    public string ApplicationName { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";

    /// <summary>Minimum ILogger level captured by TraceMonitorLoggerProvider.</summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;

    /// <summary>Capture incoming HTTP request bodies (redacted and truncated).</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Capture incoming HTTP response bodies (opt-in).</summary>
    public bool CaptureResponseBody { get; set; } = false;

    /// <summary>Capture outgoing HttpClient calls and propagate X-Trace-Id.</summary>
    public bool CaptureOutgoingHttp { get; set; } = true;

    /// <summary>Capture periodic CPU / Memory / GC / ThreadPool heartbeats.</summary>
    public bool CaptureMetrics { get; set; } = true;

    /// <summary>Heartbeat interval in seconds (default 900 = 15 minutes).</summary>
    public int MetricsIntervalSeconds { get; set; } = 900;

    /// <summary>Max captured body size in bytes (default 16 KB).</summary>
    public int MaxBodyLengthBytes { get; set; } = 16 * 1024;

    /// <summary>Max events per HTTP ingestion batch.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Flush interval in seconds for queued log events.</summary>
    public int FlushIntervalSeconds { get; set; } = 2;

    /// <summary>Bounded in-memory queue capacity (oldest dropped if full).</summary>
    public int MaxQueueCapacity { get; set; } = 5000;

    /// <summary>HTTP request path prefixes excluded from logging.</summary>
    public List<string> ExcludedPaths { get; set; } =
    [
        "/health",
        "/swagger",
        "/favicon.ico"
    ];

    /// <summary>HTTP header names whose values are masked before shipping.</summary>
    public HashSet<string> SensitiveHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Proxy-Authorization"
    };

    /// <summary>JSON property names whose values are masked in captured bodies.</summary>
    public HashSet<string> SensitiveJsonFields { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "currentPassword",
        "newPassword",
        "token",
        "accessToken",
        "refreshToken",
        "secret",
        "apiKey",
        "creditCard",
        "cvv"
    };
}

using System.Diagnostics;

namespace TraceMonitor.Client.Transport;

/// <summary>
/// Ambient trace correlation context shared across middleware, ILogger, and outgoing HttpClient calls.
/// </summary>
public static class TraceContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? TraceId
    {
        get => Current.Value ?? Activity.Current?.TraceId.ToString();
        set => Current.Value = value;
    }

    public static string? SpanId => Activity.Current?.SpanId.ToString();
}

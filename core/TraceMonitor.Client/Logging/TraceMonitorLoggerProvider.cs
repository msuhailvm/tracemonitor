using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Logging;

public sealed class TraceMonitorLoggerProvider : ILoggerProvider
{
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;
    private readonly ConcurrentDictionary<string, TraceMonitorLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public TraceMonitorLoggerProvider(
        ITraceMonitorQueue queue,
        IOptions<TraceMonitorOptions> options)
    {
        _queue = queue;
        _options = options.Value;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new TraceMonitorLogger(name, _queue, _options));

    public void Dispose() => _loggers.Clear();
}

internal sealed class TraceMonitorLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;

    public TraceMonitorLogger(string categoryName, ITraceMonitorQueue queue, TraceMonitorOptions options)
    {
        _categoryName = categoryName;
        _queue = queue;
        _options = options;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (!_options.Enabled || logLevel == LogLevel.None || logLevel < _options.MinimumLogLevel)
        {
            return false;
        }

        // Prevent internal feedback loops
        if (_categoryName.StartsWith("TraceMonitor.Client", StringComparison.OrdinalIgnoreCase) ||
            _categoryName.StartsWith("System.Net.Http.HttpClient.TraceMonitor.InternalShipper", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        Dictionary<string, object?>? properties = null;
        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            properties = [];
            foreach (var kvp in structured)
            {
                if (kvp.Key != "{OriginalFormat}")
                {
                    properties[kvp.Key] = kvp.Value;
                }
            }

            if (properties.Count == 0)
            {
                properties = null;
            }
        }

        _queue.EnqueueLog(new LogEventDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = exception is not null ? "Exception" : "Log",
            Level = MapLevel(logLevel),
            TraceId = TraceContext.TraceId,
            SpanId = TraceContext.SpanId,
            Environment = _options.Environment,
            Category = _categoryName,
            Message = message,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            StackTrace = exception?.ToString(),
            Properties = properties
        });
    }

    private static string MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => "Information"
    };
}

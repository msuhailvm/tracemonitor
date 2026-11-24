using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Telemetry;

/// <summary>
/// Captures application startup/shutdown/IIS app-pool recycle events and
/// unhandled background/unobserved task exceptions.
/// </summary>
public sealed class TraceMonitorLifecycleService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;

    private CancellationTokenRegistration _startedReg;
    private CancellationTokenRegistration _stoppingReg;

    public TraceMonitorLifecycleService(
        IHostApplicationLifetime lifetime,
        ITraceMonitorQueue queue,
        IOptions<TraceMonitorOptions> options)
    {
        _lifetime = lifetime;
        _queue = queue;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _startedReg = _lifetime.ApplicationStarted.Register(OnStarted);
        _stoppingReg = _lifetime.ApplicationStopping.Register(OnStopping);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _startedReg.Dispose();
        _stoppingReg.Dispose();
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        return Task.CompletedTask;
    }

    private void OnStarted()
    {
        _queue.EnqueueLog(new LogEventDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Lifecycle",
            Level = "Information",
            Environment = _options.Environment,
            Category = "ApplicationLifecycle",
            Message = $"Application started ({_options.ApplicationName}) on {Environment.MachineName} [PID {Environment.ProcessId}]",
            Properties = new Dictionary<string, object?>
            {
                ["event"] = "ApplicationStarted",
                ["machineName"] = Environment.MachineName,
                ["processId"] = Environment.ProcessId,
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["os"] = RuntimeInformation.OSDescription
            }
        });
    }

    private void OnStopping()
    {
        _queue.EnqueueLog(new LogEventDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Lifecycle",
            Level = "Warning",
            Environment = _options.Environment,
            Category = "ApplicationLifecycle",
            Message = $"Application stopping / recycling ({_options.ApplicationName}) on {Environment.MachineName} [PID {Environment.ProcessId}]",
            Properties = new Dictionary<string, object?>
            {
                ["event"] = "ApplicationStopping",
                ["machineName"] = Environment.MachineName,
                ["processId"] = Environment.ProcessId
            }
        });
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _queue.EnqueueLog(new LogEventDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Exception",
            Level = e.IsTerminating ? "Critical" : "Error",
            Environment = _options.Environment,
            Category = "AppDomain.UnhandledException",
            Message = $"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}): {ex?.Message ?? e.ExceptionObject?.ToString()}",
            ExceptionType = ex?.GetType().FullName,
            ExceptionMessage = ex?.Message ?? e.ExceptionObject?.ToString(),
            StackTrace = ex?.ToString()
        });
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _queue.EnqueueLog(new LogEventDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Exception",
            Level = "Error",
            Environment = _options.Environment,
            Category = "TaskScheduler.UnobservedTaskException",
            Message = $"Unobserved task exception: {e.Exception.GetBaseException().Message}",
            ExceptionType = e.Exception.GetBaseException().GetType().FullName,
            ExceptionMessage = e.Exception.GetBaseException().Message,
            StackTrace = e.Exception.ToString()
        });
    }
}

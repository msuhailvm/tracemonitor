using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Telemetry;

/// <summary>
/// Periodically captures in-process CPU %, Working Set MB, GC Heap MB, GC collection counts,
/// and ThreadPool stats without requiring OS Performance Counters (SharkASP.Net safe).
/// Default interval: 15 minutes (900 seconds).
/// </summary>
public sealed class TraceMonitorMetricsService : BackgroundService
{
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;

    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastSampleTime;
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;

    public TraceMonitorMetricsService(
        ITraceMonitorQueue queue,
        IOptions<TraceMonitorOptions> options)
    {
        _queue = queue;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.CaptureMetrics)
        {
            return;
        }

        // Baseline sample
        InitializeBaseline();

        // Send an initial heartbeat shortly after startup (10s) so the dashboard sees the app online immediately
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            CaptureSnapshot();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.MetricsIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            CaptureSnapshot();
        }
    }

    private void InitializeBaseline()
    {
        using var process = Process.GetCurrentProcess();
        _lastCpuTime = process.TotalProcessorTime;
        _lastSampleTime = DateTimeOffset.UtcNow;
        _lastGen0 = GC.CollectionCount(0);
        _lastGen1 = GC.CollectionCount(1);
        _lastGen2 = GC.CollectionCount(2);
    }

    private void CaptureSnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTimeOffset.UtcNow;
            var currentCpu = process.TotalProcessorTime;

            var elapsedMs = Math.Max(1, (now - _lastSampleTime).TotalMilliseconds);
            var cpuDeltaMs = Math.Max(0, (currentCpu - _lastCpuTime).TotalMilliseconds);
            var cpuPercent = Math.Min(100.0, (cpuDeltaMs / (elapsedMs * Environment.ProcessorCount)) * 100.0);

            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            _queue.EnqueueMetric(new MetricSnapshotDto
            {
                Timestamp = now,
                MachineName = Environment.MachineName,
                ProcessId = process.Id,
                CpuPercent = Math.Round(cpuPercent, 2),
                WorkingSetMb = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2),
                GcHeapMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
                Gen0Collections = Math.Max(0, gen0 - _lastGen0),
                Gen1Collections = Math.Max(0, gen1 - _lastGen1),
                Gen2Collections = Math.Max(0, gen2 - _lastGen2),
                ThreadCount = ThreadPool.ThreadCount,
                PendingWorkItems = ThreadPool.PendingWorkItemCount
            });

            _lastCpuTime = currentCpu;
            _lastSampleTime = now;
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;
        }
        catch
        {
            // Never impact host API stability
        }
    }
}

using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;

namespace TraceMonitor.Client.Transport;

public interface ITraceMonitorQueue
{
    bool EnqueueLog(LogEventDto logEvent);
    bool EnqueueMetric(MetricSnapshotDto metric);
    ChannelReader<LogEventDto> LogReader { get; }
    ChannelReader<MetricSnapshotDto> MetricReader { get; }
}

public sealed class TraceMonitorQueue : ITraceMonitorQueue
{
    private readonly Channel<LogEventDto> _logChannel;
    private readonly Channel<MetricSnapshotDto> _metricChannel;

    public TraceMonitorQueue(IOptions<TraceMonitorOptions> options)
    {
        var capacity = Math.Max(100, options.Value.MaxQueueCapacity);

        _logChannel = Channel.CreateBounded<LogEventDto>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _metricChannel = Channel.CreateBounded<MetricSnapshotDto>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<LogEventDto> LogReader => _logChannel.Reader;
    public ChannelReader<MetricSnapshotDto> MetricReader => _metricChannel.Reader;

    public bool EnqueueLog(LogEventDto logEvent) => _logChannel.Writer.TryWrite(logEvent);

    public bool EnqueueMetric(MetricSnapshotDto metric) => _metricChannel.Writer.TryWrite(metric);
}

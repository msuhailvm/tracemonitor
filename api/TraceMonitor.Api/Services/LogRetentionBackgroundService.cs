using Microsoft.Extensions.Options;
using TraceMonitor.Api.Configuration;
using TraceMonitor.Api.Data;

namespace TraceMonitor.Api.Services;

public sealed class LogRetentionBackgroundService : BackgroundService
{
    private readonly TraceRepository _repository;
    private readonly RetentionOptions _options;
    private readonly ILogger<LogRetentionBackgroundService> _logger;

    public LogRetentionBackgroundService(
        TraceRepository repository,
        IOptions<RetentionOptions> options,
        ILogger<LogRetentionBackgroundService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.RunIntervalHours));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var (deletedLogs, deletedMetrics) = await _repository
                    .PurgeExpiredTelemetryAsync(_options.Days, stoppingToken)
                    .ConfigureAwait(false);

                if (deletedLogs > 0 || deletedMetrics > 0)
                {
                    _logger.LogInformation(
                        "Retention cleanup purged {DeletedLogs} log_events and {DeletedMetrics} app_metrics older than {Days} days.",
                        deletedLogs, deletedMetrics, _options.Days);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retention cleanup failed; will retry next cycle.");
            }
        }
    }
}

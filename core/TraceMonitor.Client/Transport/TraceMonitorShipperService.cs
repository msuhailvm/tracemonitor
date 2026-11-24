using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;

namespace TraceMonitor.Client.Transport;

/// <summary>
/// Background worker that drains the bounded channels and ships batches to TraceMonitor.Api
/// with Gzip compression, exponential backoff on failure, and a final flush on graceful shutdown.
/// </summary>
public sealed class TraceMonitorShipperService : BackgroundService
{
    public const string InternalHttpClientName = "TraceMonitor.InternalShipper";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITraceMonitorQueue _queue;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TraceMonitorOptions _options;
    private int _consecutiveFailures;

    public TraceMonitorShipperService(
        ITraceMonitorQueue queue,
        IHttpClientFactory httpClientFactory,
        IOptions<TraceMonitorOptions> options)
    {
        _queue = queue;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsConfigured())
        {
            return;
        }

        var flushInterval = TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds));
        var batchSize = Math.Max(1, _options.BatchSize);
        var batch = new List<LogEventDto>(batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(flushInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await DrainAndShipAsync(batch, batchSize, stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (!IsConfigured())
        {
            return;
        }

        // Best-effort final flush on application shutdown (bounded to 3 seconds)
        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        flushCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var batch = new List<LogEventDto>(Math.Max(1, _options.BatchSize));
            await DrainAndShipAsync(batch, Math.Max(1, _options.BatchSize), flushCts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Never throw during host shutdown
        }
    }

    private async Task DrainAndShipAsync(List<LogEventDto> batch, int batchSize, CancellationToken ct)
    {
        // Drain log events in chunks
        while (_queue.LogReader.TryRead(out var item))
        {
            batch.Add(item);
            if (batch.Count >= batchSize)
            {
                await SendLogBatchAsync(batch, ct).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await SendLogBatchAsync(batch, ct).ConfigureAwait(false);
            batch.Clear();
        }

        // Drain metric heartbeats
        while (_queue.MetricReader.TryRead(out var metric))
        {
            await SendMetricAsync(metric, ct).ConfigureAwait(false);
        }
    }

    private async Task SendLogBatchAsync(List<LogEventDto> batch, CancellationToken ct)
    {
        if (ShouldBackoff())
        {
            return;
        }

        var url = $"{_options.Endpoint.TrimEnd('/')}/api/v1/ingest/batch";
        var payload = new BatchIngestRequest { Events = batch };
        await PostCompressedJsonAsync(url, payload, ct).ConfigureAwait(false);
    }

    private async Task SendMetricAsync(MetricSnapshotDto metric, CancellationToken ct)
    {
        if (ShouldBackoff())
        {
            return;
        }

        var url = $"{_options.Endpoint.TrimEnd('/')}/api/v1/ingest/metrics";
        await PostCompressedJsonAsync(url, metric, ct).ConfigureAwait(false);
    }

    private async Task PostCompressedJsonAsync<T>(string url, T payload, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var ms = new MemoryStream();
            await using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await gzip.WriteAsync(bytes, ct).ConfigureAwait(false);
            }

            ms.Position = 0;
            using var content = new StreamContent(ms);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Add("X-Api-Key", _options.ApiKey);

            var client = _httpClientFactory.CreateClient(InternalHttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _consecutiveFailures = 0;
            }
            else
            {
                _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 6);
            }
        }
        catch
        {
            // Fail-silent: never impact host API stability
            _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 6);
        }
    }

    private bool ShouldBackoff()
    {
        if (_consecutiveFailures == 0)
        {
            return false;
        }

        // Skip some cycles when endpoint is unreachable; always retry periodically
        var skipProbability = Math.Min(_consecutiveFailures, 5);
        return Random.Shared.Next(0, skipProbability + 1) != 0;
    }

    private bool IsConfigured() =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey);
}

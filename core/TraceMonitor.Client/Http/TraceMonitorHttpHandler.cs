using System.Diagnostics;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Middleware;
using TraceMonitor.Client.Models;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Http;

/// <summary>
/// DelegatingHandler that logs outgoing HttpClient requests (and errors) and forwards
/// X-Trace-Id for distributed tracing across your 5 .NET APIs.
/// </summary>
public sealed class TraceMonitorHttpHandler : DelegatingHandler
{
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;
    private readonly Uri? _monitorEndpointUri;

    public TraceMonitorHttpHandler(
        ITraceMonitorQueue queue,
        IOptions<TraceMonitorOptions> options)
    {
        _queue = queue;
        _options = options.Value;
        if (Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri))
        {
            _monitorEndpointUri = uri;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.CaptureOutgoingHttp || IsSelfIngestionCall(request.RequestUri))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var traceId = TraceContext.TraceId;
        if (!string.IsNullOrWhiteSpace(traceId) && !request.Headers.Contains(TraceMonitorMiddleware.TraceIdHeader))
        {
            request.Headers.TryAddWithoutValidation(TraceMonitorMiddleware.TraceIdHeader, traceId);
        }

        string? requestBody = null;
        if (_options.CaptureRequestBody && request.Content is not null)
        {
            requestBody = await ReadOutgoingRequestBodyAsync(request.Content, cancellationToken).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        Exception? capturedException = null;

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            capturedException = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            var uri = request.RequestUri;
            var statusCode = response is not null ? (int)response.StatusCode : (int?)null;
            var level = capturedException is not null || statusCode >= 500
                ? "Error"
                : statusCode >= 400
                    ? "Warning"
                    : "Information";

            var queryString = string.IsNullOrEmpty(uri?.Query) ? null : uri.Query;
            var targetUrl = $"{uri?.Authority}{uri?.AbsolutePath}{queryString}";

            _queue.EnqueueLog(new LogEventDto
            {
                Timestamp = DateTimeOffset.UtcNow,
                EventType = "OutgoingHttp",
                Level = level,
                TraceId = traceId,
                SpanId = TraceContext.SpanId,
                Environment = _options.Environment,
                HttpMethod = request.Method.Method,
                TargetHost = uri?.Authority,
                RequestPath = uri?.AbsolutePath,
                QueryString = queryString,
                StatusCode = statusCode,
                ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                Message = capturedException is not null
                    ? $"Outgoing {request.Method.Method} {targetUrl} failed: {capturedException.Message}"
                    : $"Outgoing {request.Method.Method} {targetUrl} -> {statusCode} in {sw.Elapsed.TotalMilliseconds:F1}ms",
                ExceptionType = capturedException?.GetType().FullName,
                ExceptionMessage = capturedException?.Message,
                StackTrace = capturedException?.ToString(),
                RequestBody = requestBody
            });
        }
    }

    private async Task<string?> ReadOutgoingRequestBodyAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            var mediaType = content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType) &&
                !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("text", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // LoadIntoBufferAsync ensures the content can still be sent by the inner handler after reading
            await content.LoadIntoBufferAsync().ConfigureAwait(false);
            var raw = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return BodyRedactor.RedactAndTruncateBody(raw, mediaType, _options);
        }
        catch
        {
            return null;
        }
    }

    private bool IsSelfIngestionCall(Uri? requestUri)
    {
        if (requestUri is null || _monitorEndpointUri is null)
        {
            return false;
        }

        return string.Equals(requestUri.Host, _monitorEndpointUri.Host, StringComparison.OrdinalIgnoreCase) &&
               requestUri.Port == _monitorEndpointUri.Port &&
               requestUri.AbsolutePath.StartsWith("/api/v1/ingest", StringComparison.OrdinalIgnoreCase);
    }
}

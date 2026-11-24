using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Models;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Middleware;

public sealed class TraceMonitorMiddleware
{
    public const string TraceIdHeader = "X-Trace-Id";

    private readonly RequestDelegate _next;
    private readonly ITraceMonitorQueue _queue;
    private readonly TraceMonitorOptions _options;

    public TraceMonitorMiddleware(
        RequestDelegate next,
        ITraceMonitorQueue queue,
        IOptions<TraceMonitorOptions> options)
    {
        _next = next;
        _queue = queue;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || IsExcludedPath(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var traceId = ResolveTraceId(context);
        TraceContext.TraceId = traceId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceIdHeader] = traceId;
            return Task.CompletedTask;
        });

        var sw = Stopwatch.StartNew();
        string? requestBody = null;
        string? responseBody = null;
        Exception? capturedException = null;

        if (_options.CaptureRequestBody && HasReadableBody(context.Request))
        {
            requestBody = await ReadRequestBodyAsync(context.Request).ConfigureAwait(false);
        }

        Stream? originalResponseBody = null;
        MemoryStream? responseBuffer = null;

        if (_options.CaptureResponseBody)
        {
            originalResponseBody = context.Response.Body;
            responseBuffer = new MemoryStream();
            context.Response.Body = responseBuffer;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            capturedException = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            if (responseBuffer is not null && originalResponseBody is not null)
            {
                responseBuffer.Position = 0;
                if (responseBuffer.Length > 0 && responseBuffer.Length <= _options.MaxBodyLengthBytes * 4)
                {
                    using var reader = new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true);
                    var rawResp = await reader.ReadToEndAsync().ConfigureAwait(false);
                    responseBody = BodyRedactor.RedactAndTruncateBody(rawResp, context.Response.ContentType, _options);
                }

                responseBuffer.Position = 0;
                await responseBuffer.CopyToAsync(originalResponseBody).ConfigureAwait(false);
                context.Response.Body = originalResponseBody;
                await responseBuffer.DisposeAsync().ConfigureAwait(false);
            }

            var statusCode = capturedException is not null ? 500 : context.Response.StatusCode;
            var level = capturedException is not null || statusCode >= 500
                ? "Error"
                : statusCode >= 400
                    ? "Warning"
                    : "Information";

            var logEvent = new LogEventDto
            {
                Timestamp = DateTimeOffset.UtcNow,
                EventType = capturedException is not null ? "Exception" : "Request",
                Level = level,
                TraceId = traceId,
                SpanId = TraceContext.SpanId,
                Environment = _options.Environment,
                HttpMethod = context.Request.Method,
                RequestPath = context.Request.Path.Value,
                QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                StatusCode = statusCode,
                ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ClientIp = ResolveClientIp(context),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                Message = capturedException is not null
                    ? $"{context.Request.Method} {context.Request.Path} threw {capturedException.GetType().Name}: {capturedException.Message}"
                    : $"{context.Request.Method} {context.Request.Path} responded {statusCode} in {sw.Elapsed.TotalMilliseconds:F1}ms",
                ExceptionType = capturedException?.GetType().FullName,
                ExceptionMessage = capturedException?.Message,
                StackTrace = capturedException?.ToString(),
                RequestHeaders = BodyRedactor.RedactHeaders(context.Request.Headers, _options),
                RequestBody = BodyRedactor.RedactAndTruncateBody(requestBody, context.Request.ContentType, _options),
                ResponseBody = responseBody
            };

            _queue.EnqueueLog(logEvent);
            TraceContext.TraceId = null;
        }
    }

    private bool IsExcludedPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        foreach (var prefix in _options.ExcludedPaths)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveTraceId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(TraceIdHeader, out var incoming) &&
            !string.IsNullOrWhiteSpace(incoming))
        {
            return incoming.ToString();
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }

    private static string? ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) &&
            !string.IsNullOrWhiteSpace(cfIp))
        {
            return cfIp.ToString();
        }

        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) &&
            !string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.ToString().Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static bool HasReadableBody(HttpRequest request) =>
        request.ContentLength is > 0 ||
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.Method, "PUT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.Method, "PATCH", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        try
        {
            request.EnableBuffering();
            using var reader = new StreamReader(
                request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var buffer = new char[Math.Min(_options.MaxBodyLengthBytes + 256, 65536)];
            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            request.Body.Position = 0;

            return read > 0 ? new string(buffer, 0, read) : null;
        }
        catch
        {
            return null;
        }
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TraceMonitor.Api.Auth;
using TraceMonitor.Api.Services;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Middleware;
using TraceMonitor.Client.Transport;
using Xunit;

namespace TraceMonitor.Tests;

public class CoreAndApiTests
{
    [Fact]
    public void BodyRedactor_MasksSensitiveJsonFieldsAndHeaders()
    {
        var opts = new TraceMonitorOptions();
        var rawJson = """{"username":"alice","password":"secret123","nested":{"accessToken":"tok_abc"}}""";

        var redacted = BodyRedactor.RedactAndTruncateBody(rawJson, "application/json", opts);

        Assert.NotNull(redacted);
        Assert.Contains("alice", redacted);
        Assert.DoesNotContain("secret123", redacted);
        Assert.DoesNotContain("tok_abc", redacted);
        Assert.Contains("***REDACTED***", redacted);

        var headers = new HeaderDictionary
        {
            ["Authorization"] = "Bearer xyz",
            ["X-Custom"] = "hello"
        };
        var redactedHeaders = BodyRedactor.RedactHeaders(headers, opts);
        Assert.Equal("***REDACTED***", redactedHeaders["Authorization"]);
        Assert.Equal("hello", redactedHeaders["X-Custom"]);
    }

    [Fact]
    public void SearchQueryParser_ParsesSplunkStyleTokensAndQuotedPhrases()
    {
        var parsed = SearchQueryParser.Parse("""status:500 level:err type:outgoing app:orders-api minMs:250 "connection timeout" retry""");

        Assert.Equal("500", parsed.StatusFilter);
        Assert.Equal("Error", parsed.Level);
        Assert.Equal("OutgoingHttp", parsed.EventType);
        Assert.Equal("orders-api", parsed.AppSlug);
        Assert.Equal(250, parsed.MinElapsedMs);
        Assert.Equal("connection timeout retry", parsed.CombinedFreeText);
    }

    [Fact]
    public void ApiKeyHelper_GeneratesPrefixedKeyAndDeterministicHash()
    {
        var (plain, hash, prefix) = ApiKeyHelper.GenerateApiKey();

        Assert.StartsWith("tm_live_", plain);
        Assert.Equal(plain[..14], prefix);
        Assert.Equal(hash, ApiKeyHelper.HashKey(plain));
    }

    [Fact]
    public async Task TraceMonitorMiddleware_CapturesRequestTraceIdAndCloudflareIp()
    {
        var opts = Options.Create(new TraceMonitorOptions
        {
            Enabled = true,
            Environment = "Test",
            CaptureRequestBody = false
        });
        var queue = new TraceMonitorQueue(opts);
        var middleware = new TraceMonitorMiddleware(
            async ctx =>
            {
                ctx.Response.StatusCode = 201;
                await ctx.Response.WriteAsync("ok");
            },
            queue,
            opts);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/orders";
        context.Request.Headers["X-Trace-Id"] = "trace-xyz-123";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.42";

        await middleware.InvokeAsync(context);

        Assert.True(queue.LogReader.TryRead(out var log));
        Assert.Equal("Request", log.EventType);
        Assert.Equal("Information", log.Level);
        Assert.Equal("trace-xyz-123", log.TraceId);
        Assert.Equal("203.0.113.42", log.ClientIp);
        Assert.Equal(201, log.StatusCode);
        Assert.Equal("POST", log.HttpMethod);
        Assert.Equal("/api/orders", log.RequestPath);
    }

    [Fact]
    public async Task TraceMonitorMiddleware_CapturesUnhandledExceptionAndRethrows()
    {
        var opts = Options.Create(new TraceMonitorOptions { Enabled = true });
        var queue = new TraceMonitorQueue(opts);
        var middleware = new TraceMonitorMiddleware(
            _ => throw new InvalidOperationException("Boom!"),
            queue,
            opts);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/fail";

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.True(queue.LogReader.TryRead(out var log));
        Assert.Equal("Exception", log.EventType);
        Assert.Equal("Error", log.Level);
        Assert.Equal(500, log.StatusCode);
        Assert.Equal("System.InvalidOperationException", log.ExceptionType);
        Assert.Equal("Boom!", log.ExceptionMessage);
    }
}

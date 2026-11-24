using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TraceMonitor.Client.Configuration;
using TraceMonitor.Client.Http;
using TraceMonitor.Client.Logging;
using TraceMonitor.Client.Middleware;
using TraceMonitor.Client.Telemetry;
using TraceMonitor.Client.Transport;

namespace TraceMonitor.Client.Extensions;

public static class TraceMonitorExtensions
{
    /// <summary>
    /// Registers TraceMonitor client services: bounded channel, background batch shipper,
    /// ILogger provider, lifecycle/unhandled-exception hooks, 15-min process metrics,
    /// and outgoing HttpClient tracing with X-Trace-Id propagation.
    /// </summary>
    public static WebApplicationBuilder AddTraceMonitor(
        this WebApplicationBuilder builder,
        IConfigurationSection? configurationSection = null)
    {
        var section = configurationSection ?? builder.Configuration.GetSection(TraceMonitorOptions.SectionName);
        builder.Services.Configure<TraceMonitorOptions>(section);

        builder.Services.AddTraceMonitorCore();
        return builder;
    }

    public static IServiceCollection AddTraceMonitor(
        this IServiceCollection services,
        Action<TraceMonitorOptions> configure)
    {
        services.Configure(configure);
        services.AddTraceMonitorCore();
        return services;
    }

    private static void AddTraceMonitorCore(this IServiceCollection services)
    {
        services.TryAddSingleton<ITraceMonitorQueue, TraceMonitorQueue>();

        services.AddHttpClient(TraceMonitorShipperService.InternalHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddTransient<TraceMonitorHttpHandler>();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddHttpMessageHandler<TraceMonitorHttpHandler>();
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TraceMonitorLoggerProvider>());
        services.AddHostedService<TraceMonitorShipperService>();
        services.AddHostedService<TraceMonitorLifecycleService>();
        services.AddHostedService<TraceMonitorMetricsService>();
    }

    /// <summary>
    /// Adds the TraceMonitor HTTP request &amp; exception capture middleware.
    /// Place early in the pipeline (after UseForwardedHeaders, before UseAuthorization/MapControllers).
    /// </summary>
    public static IApplicationBuilder UseTraceMonitor(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TraceMonitorMiddleware>();
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Tatkal.Observability;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires distributed tracing (ASP.NET Core + HttpClient + Npgsql
    /// instrumentation, exported to Jaeger/Tempo over OTLP) and a
    /// Prometheus metrics endpoint, the same way in every service (spec
    /// sections 25-26). CorrelationId is propagated via the
    /// X-Correlation-Id header set at the gateway and enriched into every
    /// Activity's tags here so a trace and a log line can be joined on it
    /// even without a full tracing backend running.
    /// </summary>
    public static IServiceCollection AddTatkalObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otlpEndpoint = configuration["Otel:OtlpEndpoint"] ?? "http://jaeger:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(serviceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(serviceName)
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>Reads/generates X-Correlation-Id and pushes it into the
    /// current Activity's tags so every span in a trace carries it.</summary>
    public static IApplicationBuilder UseTatkalCorrelationId(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            System.Diagnostics.Activity.Current?.SetTag("correlation_id", correlationId);
            await next();
        });
    }

    /// <summary>Exposes /metrics for Prometheus to scrape.</summary>
    public static WebApplication MapTatkalMetrics(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}

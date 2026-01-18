using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpenTelemetryConfigs;

public static class DependencyInjection
{
    public static IHostApplicationBuilder ConfigureCommon(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddLogging();
        builder.Services.AddSingleton(new ActivitySource("Observability.ActivitySource"));

        builder.ConfigureOpenTelemetry(serviceName);

        return builder;
    }

    private static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: "1.0.0")
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", environment),
                new KeyValuePair<string, object>("host.name", Environment.MachineName)
            });

        // Configuração do OTLP Exporter para receber logs, traces e métricas
        Action<OtlpExporterOptions> otlpExporter = exporterOptions =>
        {
            exporterOptions.Endpoint = new Uri("http://localhost:4317");
            exporterOptions.Protocol = OtlpExportProtocol.Grpc;
        };

        // Configuração do OpenTelemetry Logging
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            logging.AddOtlpExporter(otlpExporter);
            logging.AddConsoleExporter();
        });

        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
                                              | ActivityTrackingOptions.TraceId
                                              | ActivityTrackingOptions.ParentId
                                              | ActivityTrackingOptions.Baggage
                                              | ActivityTrackingOptions.Tags;
        });

        // Configuração do OpenTelemetry Tracing e Metrics
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(serviceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddSqlClientInstrumentation(opt => opt.SetDbStatementForText = true)
                    .AddRedisInstrumentation(opt => opt.SetVerboseDatabaseStatements = true)
                    .AddOtlpExporter(otlpExporter)
                    .AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddMeter("System.Net.Http")
                    .AddMeter("System.Net.NameResolution")
                    .AddMeter(serviceName)
                    .AddOtlpExporter(otlpExporter)
                    .AddConsoleExporter();
            });
    }
}
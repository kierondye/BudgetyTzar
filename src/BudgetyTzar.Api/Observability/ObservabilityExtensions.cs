using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BudgetyTzar.Api.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddBudgetyTzarObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        services.AddSingleton<BudgetyTzarTelemetry>();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(BudgetyTzarTelemetry.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(BudgetyTzarTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (options.ConsoleExporterEnabled)
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = new Uri(options.OtlpEndpoint);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(BudgetyTzarTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (options.ConsoleExporterEnabled)
                {
                    metrics.AddConsoleExporter();
                }

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = new Uri(options.OtlpEndpoint);
                    });
                }
            });

        return services;
    }

    public static IApplicationBuilder UseBudgetyTzarObservability(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}

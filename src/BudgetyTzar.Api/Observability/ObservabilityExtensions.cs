using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BudgetyTzar.Api.Observability;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddBudgetyTzarObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BudgetyTzarTelemetry>();

        var openTelemetry = builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                BudgetyTzarTelemetry.ServiceName,
                serviceVersion: RuntimeVersion.Current.ProductVersion))
            .WithTracing(tracing => tracing
                .AddSource(BudgetyTzarTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddProcessor(new PrivacyActivityProcessor()))
            .WithMetrics(metrics => metrics
                .AddMeter(BudgetyTzarTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        if (builder.Configuration.GetValue<bool>("OpenTelemetry:ConsoleExporter:Enabled"))
        {
            openTelemetry
                .WithTracing(tracing => tracing.AddConsoleExporter())
                .WithMetrics(metrics => metrics.AddConsoleExporter());
            builder.Logging.AddOpenTelemetry(options => options.AddConsoleExporter());
        }

        if (builder.Configuration.GetValue<bool>("OpenTelemetry:OtlpExporter:Enabled"))
        {
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }

    private sealed class PrivacyActivityProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            // Server route templates remain available through http.route. Raw paths and
            // query strings may contain resource identifiers or financial search data.
            if (activity.Kind == ActivityKind.Server)
            {
                activity.SetTag("url.path", null);
                activity.SetTag("url.query", null);
                activity.SetTag("http.target", null);
            }
        }
    }
}

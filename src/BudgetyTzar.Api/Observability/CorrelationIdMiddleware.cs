using Microsoft.AspNetCore.Http.Features;

namespace BudgetyTzar.Api.Observability;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    BudgetyTzarTelemetry telemetry,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context.Request.Headers[HeaderName]);
        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var activity = telemetry.ActivitySource.StartActivity("http.request");
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("http.request.method", context.Request.Method);

        var endpointName = EndpointName(context);
        activity?.SetTag("endpoint", endpointName);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        var start = TimeProvider.System.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = TimeProvider.System.GetElapsedTime(start);
            var statusCode = context.Response.StatusCode;
            var completedEndpointName = EndpointName(context);

            telemetry.RecordRequest(
                completedEndpointName,
                context.Request.Method,
                statusCode,
                elapsed);

            logger.LogInformation(
                "HTTP request completed for {EndpointName} with status {StatusCode} in {ElapsedMilliseconds} ms.",
                completedEndpointName,
                statusCode,
                elapsed.TotalMilliseconds);
        }
    }

    private static string GetOrCreateCorrelationId(IReadOnlyList<string?> values)
    {
        var candidate = values.Count == 1 ? values[0] : null;

        return IsValidCorrelationId(candidate)
            ? candidate!
            : Guid.NewGuid().ToString("N");
    }

    private static bool IsValidCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string EndpointName(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var name = endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;

        return string.IsNullOrWhiteSpace(name)
            ? "unmatched"
            : name;
    }
}

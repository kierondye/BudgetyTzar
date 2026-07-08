using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;

namespace BudgetyTzar.Api.Observability;

public sealed partial class RequestObservabilityMiddleware(
    RequestDelegate next,
    ILogger<RequestObservabilityMiddleware> logger)
{
    public const string CorrelationHeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, BudgetyTzarTelemetry telemetry)
    {
        var correlationId = GetCorrelationId(context.Request);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeaderName] = correlationId;
            return Task.CompletedTask;
        });

        Activity.Current?.SetTag("correlation.id", correlationId);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty
        });

        var started = Stopwatch.GetTimestamp();
        int? unhandledErrorStatusCode = null;

        try
        {
            await next(context);
        }
        catch (Exception)
        {
            unhandledErrorStatusCode = StatusCodes.Status500InternalServerError;
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var route = GetStableRoute(context);
            var statusCode = unhandledErrorStatusCode ?? context.Response.StatusCode;

            telemetry.RecordRequest(context.Request.Method, route, statusCode, elapsedSeconds);

            if (statusCode == StatusCodes.Status400BadRequest)
            {
                telemetry.RecordValidationFailure(route);
            }

            logger.LogInformation(
                "HTTP request completed {RequestMethod} {Route} with {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                route,
                statusCode,
                elapsedSeconds * 1000);
        }
    }

    private static string GetCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(CorrelationHeaderName, out var values)
            && values.Count == 1
            && CorrelationIdPattern().IsMatch(values[0]!))
        {
            return values[0]!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string GetStableRoute(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? "unknown"
            : "unmatched";
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BudgetyTzar.Api.Observability;

public sealed class BudgetyTzarTelemetry : IDisposable
{
    public const string ServiceName = "BudgetyTzar.Api";
    public const string MeterName = ServiceName;
    public const string ActivitySourceName = ServiceName;

    public const string RequestCountName = "budgetytzar.api.requests";
    public const string RequestErrorCountName = "budgetytzar.api.errors";
    public const string RequestDurationName = "budgetytzar.api.request.duration";
    public const string ValidationFailureCountName = "budgetytzar.validation.failures";
    public const string AllocationFailureCountName = "budgetytzar.transaction.allocation.failures";
    public const string BudgetSummaryDurationName = "budgetytzar.budget_summary.duration";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> requestCount;
    private readonly Counter<long> requestErrorCount;
    private readonly Histogram<double> requestDuration;
    private readonly Counter<long> validationFailureCount;
    private readonly Counter<long> allocationFailureCount;
    private readonly Histogram<double> budgetSummaryDuration;

    public BudgetyTzarTelemetry()
    {
        requestCount = meter.CreateCounter<long>(RequestCountName, description: "Completed API requests.");
        requestErrorCount = meter.CreateCounter<long>(RequestErrorCountName, description: "API requests returning an error.");
        requestDuration = meter.CreateHistogram<double>(
            RequestDurationName,
            unit: "s",
            description: "API request duration.");
        validationFailureCount = meter.CreateCounter<long>(
            ValidationFailureCountName,
            description: "Requests rejected by validation.");
        allocationFailureCount = meter.CreateCounter<long>(
            AllocationFailureCountName,
            description: "Rejected transaction allocation attempts.");
        budgetSummaryDuration = meter.CreateHistogram<double>(
            BudgetSummaryDurationName,
            unit: "s",
            description: "Budget Summary query duration.");
    }

    public static Activity? StartBudgetSummaryQuery()
    {
        return ActivitySources.Source.StartActivity("budget_summary.query", ActivityKind.Internal);
    }

    public void RecordRequest(string method, string route, int statusCode, double elapsedSeconds)
    {
        var tags = new TagList
        {
            { "http.request.method", method },
            { "http.route", route },
            { "http.response.status_code", statusCode }
        };

        requestCount.Add(1, tags);
        requestDuration.Record(elapsedSeconds, tags);

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            requestErrorCount.Add(1, tags);
        }
    }

    public void RecordValidationFailure(string route)
    {
        validationFailureCount.Add(1, new KeyValuePair<string, object?>("http.route", route));
        Activity.Current?.AddEvent(new ActivityEvent("validation.failed"));
    }

    public void RecordAllocationFailure(string reason)
    {
        allocationFailureCount.Add(1, new KeyValuePair<string, object?>("failure.reason", reason));
        Activity.Current?.AddEvent(
            new ActivityEvent(
                "transaction.allocation.failed",
                tags: new ActivityTagsCollection
                {
                    { "failure.reason", reason }
                }));
    }

    public void RecordBudgetSummary(double elapsedSeconds, string outcome)
    {
        budgetSummaryDuration.Record(
            elapsedSeconds,
            new KeyValuePair<string, object?>("query.outcome", outcome));
    }

    public void Dispose()
    {
        meter.Dispose();
    }

    private static class ActivitySources
    {
        internal static readonly ActivitySource Source = new(ActivitySourceName);
    }
}

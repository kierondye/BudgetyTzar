using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BudgetyTzar.Api.Observability;

public sealed class ApiTelemetry
{
    public const string ServiceName = "BudgetyTzar.Api";
    public const string MeterName = "BudgetyTzar.Api";
    public const string ActivitySourceName = "BudgetyTzar.Api";

    public const string RequestCounterName = "budgetytzar.api.requests";
    public const string RequestErrorCounterName = "budgetytzar.api.request_errors";
    public const string RequestLatencyHistogramName = "budgetytzar.api.request.duration";
    public const string ValidationFailureCounterName = "budgetytzar.api.validation_failures";
    public const string AllocationFailureCounterName = "budgetytzar.api.transaction_allocation_failures";
    public const string BudgetSummaryLatencyHistogramName = "budgetytzar.api.budget_summary.duration";

    public const string EndpointTag = "endpoint";
    public const string MethodTag = "method";
    public const string StatusCodeTag = "status_code";
    public const string FailureKindTag = "failure_kind";

    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Counter<long> _requestErrorCounter;
    private readonly Histogram<double> _requestLatency;
    private readonly Counter<long> _validationFailureCounter;
    private readonly Counter<long> _allocationFailureCounter;
    private readonly Histogram<double> _budgetSummaryLatency;

    public ApiTelemetry()
        : this(new Meter(MeterName))
    {
    }

    private ApiTelemetry(Meter meter)
    {
        _meter = meter;
        ActivitySource = new ActivitySource(ActivitySourceName);
        _requestCounter = _meter.CreateCounter<long>(
            RequestCounterName,
            unit: "{request}",
            description: "API request count by stable endpoint name.");
        _requestErrorCounter = _meter.CreateCounter<long>(
            RequestErrorCounterName,
            unit: "{request}",
            description: "API request error count by stable endpoint name.");
        _requestLatency = _meter.CreateHistogram<double>(
            RequestLatencyHistogramName,
            unit: "ms",
            description: "API request latency by stable endpoint name.");
        _validationFailureCounter = _meter.CreateCounter<long>(
            ValidationFailureCounterName,
            unit: "{failure}",
            description: "Validation failure count by endpoint and low-cardinality failure kind.");
        _allocationFailureCounter = _meter.CreateCounter<long>(
            AllocationFailureCounterName,
            unit: "{failure}",
            description: "Transaction allocation failure count by low-cardinality failure kind.");
        _budgetSummaryLatency = _meter.CreateHistogram<double>(
            BudgetSummaryLatencyHistogramName,
            unit: "ms",
            description: "Budget Summary query latency.");
    }

    public ActivitySource ActivitySource { get; }

    public void RecordRequest(string endpointName, string method, int statusCode, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { EndpointTag, endpointName },
            { MethodTag, method },
            { StatusCodeTag, statusCode.ToString("000") }
        };

        _requestCounter.Add(1, tags);
        _requestLatency.Record(elapsed.TotalMilliseconds, tags);

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            _requestErrorCounter.Add(1, tags);
        }
    }

    public void RecordValidationFailure(string endpointName, string failureKind)
    {
        _validationFailureCounter.Add(
            1,
            new KeyValuePair<string, object?>(EndpointTag, endpointName),
            new KeyValuePair<string, object?>(FailureKindTag, failureKind));
    }

    public void RecordAllocationFailure(string failureKind)
    {
        _allocationFailureCounter.Add(
            1,
            new KeyValuePair<string, object?>(FailureKindTag, failureKind));
    }

    public void RecordBudgetSummaryLatency(TimeSpan elapsed)
    {
        _budgetSummaryLatency.Record(elapsed.TotalMilliseconds);
    }
}

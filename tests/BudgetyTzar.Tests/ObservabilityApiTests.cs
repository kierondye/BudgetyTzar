using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api.Observability;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests;

public sealed class ObservabilityApiTests
{
    [Fact]
    public async Task Responses_include_a_generated_correlation_id_when_request_does_not_supply_one()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.True(IsValidCorrelationId(correlationId));
    }

    [Fact]
    public async Task Swagger_responses_include_a_generated_correlation_id()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.True(IsValidCorrelationId(correlationId));
    }

    [Fact]
    public async Task Responses_propagate_a_valid_incoming_correlation_id()
    {
        await using var server = await TestApiServer.StartAsync();
        const string correlationId = "client-request-123.ABC_def";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        using var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId, Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task Responses_replace_an_invalid_incoming_correlation_id()
    {
        await using var server = await TestApiServer.StartAsync();
        const string invalidCorrelationId = "client request with spaces";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, invalidCorrelationId);

        using var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generatedCorrelationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.NotEqual(invalidCorrelationId, generatedCorrelationId);
        Assert.True(IsValidCorrelationId(generatedCorrelationId));
    }

    [Fact]
    public async Task Request_metrics_use_stable_endpoint_names()
    {
        using var metrics = new MetricRecorder(
            ApiTelemetry.RequestCounterName,
            ApiTelemetry.RequestLatencyHistogramName);
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(metrics.HasSample(
            ApiTelemetry.RequestCounterName,
            (ApiTelemetry.EndpointTag, "GetVersion"),
            (ApiTelemetry.MethodTag, "GET"),
            (ApiTelemetry.StatusCodeTag, "200")));
        Assert.True(metrics.HasSample(
            ApiTelemetry.RequestLatencyHistogramName,
            (ApiTelemetry.EndpointTag, "GetVersion"),
            (ApiTelemetry.MethodTag, "GET"),
            (ApiTelemetry.StatusCodeTag, "200")));
    }

    [Fact]
    public async Task Validation_failures_emit_low_cardinality_telemetry()
    {
        using var metrics = new MetricRecorder(ApiTelemetry.ValidationFailureCounterName);
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest("", "gbp"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(metrics.HasSample(
            ApiTelemetry.ValidationFailureCounterName,
            (ApiTelemetry.EndpointTag, "CreateBudget"),
            (ApiTelemetry.FailureKindTag, "request_validation")));
    }

    [Fact]
    public async Task Transaction_allocation_failures_emit_low_cardinality_telemetry()
    {
        using var metrics = new MetricRecorder(ApiTelemetry.AllocationFailureCounterName);
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}/allocation",
            new AllocateTransactionRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(metrics.HasSample(
            ApiTelemetry.AllocationFailureCounterName,
            (ApiTelemetry.FailureKindTag, "transaction_not_found")));
    }

    [Fact]
    public async Task Budget_summary_queries_emit_latency_telemetry()
    {
        using var metrics = new MetricRecorder(ApiTelemetry.BudgetSummaryLatencyHistogramName);
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server.Client, "UK", "GBP");

        using var response = await server.Client.GetAsync($"/api/budgets/{budget.BudgetId}/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(metrics.HasSample(ApiTelemetry.BudgetSummaryLatencyHistogramName));
    }

    private static bool IsValidCorrelationId(string value)
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

    private static async Task<BudgetResponse> CreateBudgetAsync(HttpClient client, string name, string currency)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest(name, currency));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BudgetResponse>())!;
    }

    private sealed class MetricRecorder : IDisposable
    {
        private readonly HashSet<string> _instrumentNames;
        private readonly List<MetricSample> _samples = [];
        private readonly object _sync = new();
        private readonly MeterListener _listener = new();

        public MetricRecorder(params string[] instrumentNames)
        {
            _instrumentNames = instrumentNames.ToHashSet(StringComparer.Ordinal);
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApiTelemetry.MeterName &&
                    _instrumentNames.Contains(instrument.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                Record(instrument, measurement, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
                Record(instrument, measurement, tags));
            _listener.Start();
        }

        public bool HasSample(string instrumentName, params (string Name, string Value)[] expectedTags)
        {
            lock (_sync)
            {
                return _samples.Any(sample =>
                    sample.InstrumentName == instrumentName &&
                    expectedTags.All(tag =>
                        sample.Tags.TryGetValue(tag.Name, out var value) &&
                        value == tag.Value));
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void Record<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            var copiedTags = tags.ToArray()
                .ToDictionary(
                    tag => tag.Key,
                    tag => tag.Value?.ToString() ?? string.Empty,
                    StringComparer.Ordinal);

            lock (_sync)
            {
                _samples.Add(new MetricSample(instrument.Name, copiedTags));
            }
        }
    }

    private sealed record MetricSample(string InstrumentName, IReadOnlyDictionary<string, string> Tags);

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record BudgetResponse(Guid BudgetId, string Name, string Currency);

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);
}

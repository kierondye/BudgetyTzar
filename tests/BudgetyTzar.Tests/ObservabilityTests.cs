using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BudgetyTzar.Api.Observability;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests;

public sealed partial class ObservabilityTests
{
    [Fact]
    public async Task Valid_incoming_correlation_id_is_returned()
    {
        await using var server = await TestApiServer.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(RequestObservabilityMiddleware.CorrelationHeaderName, "client-request_123");

        using var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "client-request_123",
            Assert.Single(response.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeaderName)));
    }

    [Fact]
    public async Task Invalid_incoming_correlation_id_is_replaced()
    {
        await using var server = await TestApiServer.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(RequestObservabilityMiddleware.CorrelationHeaderName, "contains spaces and is not trusted");

        using var response = await server.Client.SendAsync(request);

        var correlationId = Assert.Single(
            response.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeaderName));
        Assert.NotEqual("contains spaces and is not trusted", correlationId);
        Assert.Matches(GeneratedCorrelationIdPattern(), correlationId);
    }

    [Fact]
    public async Task Custom_metrics_cover_validation_allocation_and_budget_summary_without_resource_ids()
    {
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = CreateListener(measurements);
        listener.Start();
        await using var server = await TestApiServer.StartAsync();

        using var validationResponse = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new
            {
                description = "",
                type = "Credit",
                transactionDate = "2026-07-01",
                amount = "10.00",
                currency = "GBP"
            });
        Assert.Equal(HttpStatusCode.BadRequest, validationResponse.StatusCode);

        var missingTransactionId = Guid.NewGuid();
        using var allocationResponse = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{missingTransactionId}/allocation",
            new { budgetItemId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, allocationResponse.StatusCode);

        var missingBudgetId = Guid.NewGuid();
        using var summaryResponse = await server.Client.GetAsync($"/api/budgets/{missingBudgetId}/summary");
        Assert.Equal(HttpStatusCode.NotFound, summaryResponse.StatusCode);

        Assert.Contains(
            measurements,
            measurement => measurement.Name == BudgetyTzarTelemetry.ValidationFailureCountName
                && measurement.Tags["http.route"] == "/api/transactions/");
        Assert.Contains(
            measurements,
            measurement => measurement.Name == BudgetyTzarTelemetry.AllocationFailureCountName
                && measurement.Tags["failure.reason"] == "transaction_not_found");
        Assert.Contains(
            measurements,
            measurement => measurement.Name == BudgetyTzarTelemetry.BudgetSummaryDurationName
                && measurement.Tags["query.outcome"] == "not_found");

        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == BudgetyTzarTelemetry.RequestCountName
                && measurement.Tags.GetValueOrDefault("http.route") == "/api/budgets/{budgetId:guid}/summary");
        Assert.DoesNotContain(missingBudgetId.ToString(), string.Join(' ', measurements.SelectMany(x => x.Tags.Values)));
        Assert.DoesNotContain(missingTransactionId.ToString(), string.Join(' ', measurements.SelectMany(x => x.Tags.Values)));
    }

    private static MeterListener CreateListener(ConcurrentQueue<Measurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == BudgetyTzarTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => measurements.Enqueue(
                new Measurement(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => measurements.Enqueue(
                new Measurement(instrument.Name, value, CopyTags(tags))));

        return listener;
    }

    private static Dictionary<string, string> CopyTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return copied;
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedCorrelationIdPattern();

    private sealed record Measurement(string Name, double Value, Dictionary<string, string> Tags);
}

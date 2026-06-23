using System.Net;
using System.Text.Json;

namespace BudgetyTzar.Tests;

public sealed class CanonicalApiSurfaceTests
{
    [Fact]
    public async Task SwaggerOnlyAdvertisesCanonicalBudgetingRoutes()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();

        using var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var orderedPaths = swagger.RootElement.GetProperty("paths").EnumerateObject().Select(x => x.Name).ToList();
        var paths = orderedPaths.ToHashSet();

        Assert.Contains("/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments", paths);
        Assert.Contains("/api/budgets/{budgetId}/reallocations", paths);
        Assert.Contains("/api/budgets/{budgetId}/transactions/{transactionId}/allocations", paths);
        Assert.Contains("/api/budgets/{budgetId}/snapshot", paths);
        Assert.Contains("/api/budgets/{budgetId}/audit-events", paths);

        Assert.DoesNotContain("/api/budgets/{budgetId}/periods", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/for-date", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/adjustments", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/reallocations", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/allocations", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/budget-lines", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/transactions/{transactionId}/assignments", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/activity", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/activity.csv", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/audit-timeline", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/budget-line-trends", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/reconciliation", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/period-summary", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/period-summary.csv", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/budget-item-trends", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/credit-variance", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/reconciliation/date-range", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/audit-timeline/date-range", paths);
        Assert.Equal(orderedPaths.Order(StringComparer.Ordinal).ToList(), orderedPaths);

        var transactionList = swagger.RootElement
            .GetProperty("paths")
            .GetProperty("/api/budgets/{budgetId}/transactions")
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("allocationStatus", transactionList);
        Assert.DoesNotContain("assignmentStatus", transactionList);
    }

    [Fact]
    public async Task RemovedRoutesReturnNotFound()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        var budgetId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync($"/api/budgets/{budgetId}/periods"),
            await client.GetAsync($"/api/budgets/{budgetId}/periods/for-date?date=2026-06-01"),
            await client.GetAsync($"/api/budgets/{budgetId}/periods/{periodId}"),
            await client.GetAsync($"/api/budgets/{budgetId}/periods/{periodId}/adjustments"),
            await client.GetAsync($"/api/budgets/{budgetId}/periods/{periodId}/reallocations"),
            await client.GetAsync($"/api/budgets/{budgetId}/periods/{periodId}/allocations"),
            await client.GetAsync($"/api/budgets/{budgetId}/budget-lines"),
            await client.GetAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/assignments"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/activity?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/activity.csv?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/budget-line-trends?budgetLineId={Guid.NewGuid()}&from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/reconciliation?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/audit-timeline?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/period-summary?periodId={periodId}"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/period-summary.csv?periodId={periodId}"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/budget-item-trends?budgetItemId={Guid.NewGuid()}&from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/credit-variance?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/reconciliation/date-range?from=2026-06-01&to=2026-06-30"),
            await client.GetAsync($"/api/budgets/{budgetId}/reports/audit-timeline/date-range?from=2026-06-01&to=2026-06-30")
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }
}

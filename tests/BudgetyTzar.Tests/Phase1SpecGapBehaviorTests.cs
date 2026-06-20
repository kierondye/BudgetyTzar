using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Features;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class Phase1SpecGapBehaviorTests
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

    [Fact]
    public async Task CanonicalBudgetItemApiCreatesNameOnlyItemsAndHidesLegacyDirectionFields()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items",
            new CreateBudgetItemRequest("Groceries"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Groceries", json);
        Assert.DoesNotContain("direction", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rolloverType", json, StringComparison.OrdinalIgnoreCase);

        var items = await client.GetFromJsonAsync<IReadOnlyList<BudgetItemDto>>(
            $"/api/budgets/{budget.Id}/budget-items");
        var item = Assert.Single(items!);
        Assert.Equal("Groceries", item.Name);
    }

    [Fact]
    public async Task CanonicalAdjustmentsSnapshotAndArchivedHistoryUsePlannedVsActualBalances()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        var retired = await CreateBudgetItem(client, budget.Id, "Retired");

        await RecordBudgetItemAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Expected salary");
        await RecordBudgetItemAdjustment(client, budget.Id, groceries.Id, 500m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 2), "Expected groceries");
        await RecordBudgetItemAdjustment(client, budget.Id, retired.Id, 10m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 2), "Historical balance");
        await client.PostAsync($"/api/budgets/{budget.Id}/budget-items/{retired.Id}/archive", null);

        var beforeDebit = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-01");
        var afterDebit = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-02");

        Assert.Equal(-2_500m, beforeDebit!.BudgetItems.Single(x => x.BudgetItemId == salary.Id).Balance);
        Assert.DoesNotContain(beforeDebit.BudgetItems, x => x.BudgetItemId == groceries.Id && x.Balance != 0);
        Assert.Equal(500m, afterDebit!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
        Assert.Equal(-10m, afterDebit.BudgetItems.Single(x => x.BudgetItemId == retired.Id).Balance);
    }

    [Fact]
    public async Task CanonicalBudgetSnapshotTracksPlannedActualAndUnbudgetedBalances()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        var mortgage = await CreateBudgetItem(client, budget.Id, "Mortgage");
        var incidentals = await CreateBudgetItem(client, budget.Id, "Incidentals");

        await RecordBudgetItemAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 18), "Initial budget for salary.");
        await RecordBudgetItemAdjustment(client, budget.Id, groceries.Id, 500m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for groceries.");
        await RecordBudgetItemAdjustment(client, budget.Id, mortgage.Id, 800m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for mortgage.");
        await RecordBudgetItemAdjustment(client, budget.Id, incidentals.Id, 1_000m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 18), "Initial budget for incidentals.");

        var initialSnapshot = await GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 18));
        AssertSnapshot(
            initialSnapshot,
            -200m,
            0m,
            0m,
            -200m,
            [
                (salary.Id, -2_500m, 2_500m, 0m, 0m, 0m),
                (groceries.Id, 500m, 0m, 500m, 0m, 0m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 1_000m, 0m, 1_000m, 0m, 0m)
            ]);

        var pay = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 19), 3_000m, TransactionDirection.Credit);
        await ReplaceAllocations(client, budget.Id, pay.Id, [new TransactionAllocationItem(salary.Id, 3_000m)]);

        var afterSalarySnapshot = await GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 19));
        AssertSnapshot(
            afterSalarySnapshot,
            3_000m,
            200m,
            3_000m,
            2_800m,
            [
                (salary.Id, 500m, 2_500m, 0m, 3_000m, 0m),
                (groceries.Id, 500m, 0m, 500m, 0m, 0m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 1_000m, 0m, 1_000m, 0m, 0m)
            ]);

        var supermarket = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 200m, TransactionDirection.Debit);
        await ReplaceAllocations(client, budget.Id, supermarket.Id, [
            new TransactionAllocationItem(groceries.Id, 150m),
            new TransactionAllocationItem(incidentals.Id, 40m)
        ]);

        var afterSpendSnapshot = await GetSnapshot(client, budget.Id, new DateOnly(2026, 6, 21));
        AssertSnapshot(
            afterSpendSnapshot,
            2_800m,
            190m,
            2_800m,
            2_610m,
            [
                (salary.Id, 500m, 2_500m, 0m, 3_000m, 0m),
                (groceries.Id, 350m, 0m, 500m, 0m, 150m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 960m, 0m, 1_000m, 0m, 40m)
            ]);

        await RecordBudgetItemAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 7, 18), "Second expected salary.");

        var julySnapshot = await GetSnapshot(client, budget.Id, new DateOnly(2026, 7, 18));
        AssertSnapshot(
            julySnapshot,
            300m,
            190m,
            2_800m,
            110m,
            [
                (salary.Id, -2_000m, 5_000m, 0m, 3_000m, 0m),
                (groceries.Id, 350m, 0m, 500m, 0m, 150m),
                (mortgage.Id, 800m, 0m, 800m, 0m, 0m),
                (incidentals.Id, 960m, 0m, 1_000m, 0m, 40m)
            ]);
    }

    [Fact]
    public async Task CanonicalGroupedReallocationRequiresZeroSumAndCreatesLinkedAdjustments()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var dining = await CreateBudgetItem(client, budget.Id, "Dining");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");

        var invalid = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 5),
                "Unbalanced",
                [
                    new BudgetReallocationAdjustmentItem(dining.Id, 30m, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(groceries.Id, 20m, BudgetAdjustmentType.Debit)
                ]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 5),
                "Move budget",
                [
                    new BudgetReallocationAdjustmentItem(dining.Id, 30m, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(groceries.Id, 30m, BudgetAdjustmentType.Debit)
                ]));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        var reallocations = await client.GetFromJsonAsync<IReadOnlyList<BudgetReallocationDto>>(
            $"/api/budgets/{budget.Id}/reallocations");
        var reallocation = Assert.Single(reallocations!);
        Assert.Equal(2, reallocation.Adjustments.Count);

        var snapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-05");
        Assert.Equal(-30m, snapshot!.BudgetItems.Single(x => x.BudgetItemId == dining.Id).Balance);
        Assert.Equal(30m, snapshot.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
    }

    [Fact]
    public async Task ArchivedBudgetItemsOnlyAllowRetrospectiveCorrectionsOnOrBeforeArchiveDate()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var retired = await CreateBudgetItem(client, budget.Id, "Retired");
        var archiveDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var archive = await client.PostAsync($"/api/budgets/{budget.Id}/budget-items/{retired.Id}/archive", null);
        archive.EnsureSuccessStatusCode();

        var retrospective = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{retired.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(10m, BudgetAdjustmentType.Credit, archiveDate, "Retrospective correction"));
        var future = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{retired.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(10m, BudgetAdjustmentType.Credit, archiveDate.AddDays(1), "Future archived activity"));

        Assert.Equal(HttpStatusCode.Created, retrospective.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task ArchivedBudgetItemsRejectFutureTransactionAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var retired = await CreateBudgetItem(client, budget.Id, "Retired");
        var futureDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(1);
        await client.PostAsync($"/api/budgets/{budget.Id}/budget-items/{retired.Id}/archive", null);
        var transaction = await CreateTransaction(client, budget.Id, futureDate, 12m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(retired.Id, 12m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NetPlannedSpendingValidationIsScopedToTheRelevantDate()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");

        await RecordBudgetItemAdjustment(client, budget.Id, salary.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 7, 1), "Future income");
        var earlyDebit = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 1), "Too early"));
        var laterDebit = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{groceries.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(100m, BudgetAdjustmentType.Debit, new DateOnly(2026, 7, 2), "After income"));

        Assert.Equal(HttpStatusCode.BadRequest, earlyDebit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, laterDebit.StatusCode);
    }

    [Fact]
    public async Task AllocationStatusQueryFiltersTransactionList()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        var unallocated = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 10m, TransactionDirection.Debit);
        var allocated = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 21), 10m, TransactionDirection.Debit);
        await ReplaceAllocations(client, budget.Id, allocated.Id, [new TransactionAllocationItem(groceries.Id, 10m)]);

        var transactions = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?allocationStatus=unallocated");

        var transaction = Assert.Single(transactions!);
        Assert.Equal(unallocated.Id, transaction.Id);
    }

    [Fact]
    public async Task AuditEventsEndpointReturnsDurableLocalAuditTimeline()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        await RecordBudgetItemAdjustment(client, budget.Id, groceries.Id, 100m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Initial groceries.");

        var events = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/budgets/{budget.Id}/audit-events");

        Assert.NotNull(events);
        Assert.Contains(events!, x => x.EventType == "BudgetCreated" && x.EntityId == budget.Id);
        Assert.Contains(events!, x => x.EventType == "BudgetItemCreated" && x.EntityId == groceries.Id);
        Assert.Contains(events!, x => x.EventType == "BudgetAdjustmentRecorded");
    }

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetItemDto> CreateBudgetItem(HttpClient client, Guid budgetId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetItemDto>())!;
    }

    private static async Task RecordBudgetItemAdjustment(
        HttpClient client,
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string notes)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments",
            new CreateBudgetItemAdjustmentRequest(amount, type, date, notes));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<FinancialTransaction> CreateTransaction(
        HttpClient client,
        Guid budgetId,
        DateOnly transactionDate,
        decimal amount,
        TransactionDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(
                transactionDate,
                $"{direction} transaction",
                amount,
                direction,
                "Current account",
                null,
                null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }

    private static async Task<BudgetSnapshot> GetSnapshot(HttpClient client, Guid budgetId, DateOnly date)
    {
        return (await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budgetId}/snapshot?date={date:yyyy-MM-dd}"))!;
    }

    private static void AssertSnapshot(
        BudgetSnapshot snapshot,
        decimal totalBalance,
        decimal unbudgetedBalance,
        decimal totalTransactionBalance,
        decimal totalBudgetedBalance,
        IReadOnlyList<(
            Guid BudgetItemId,
            decimal Balance,
            decimal PlannedCredit,
            decimal PlannedDebit,
            decimal ActualCredit,
            decimal ActualDebit)> expectedBalances)
    {
        Assert.Equal(totalBalance, snapshot.TotalBalance);
        Assert.Equal(unbudgetedBalance, snapshot.UnbudgetedBalance);
        Assert.Equal(totalTransactionBalance, snapshot.TotalTransactionBalance);
        Assert.Equal(totalBudgetedBalance, snapshot.TotalBudgetedBalance);
        foreach (var expected in expectedBalances)
        {
            var item = snapshot.BudgetItems.Single(x => x.BudgetItemId == expected.BudgetItemId);
            Assert.Equal(expected.Balance, item.Balance);
            Assert.Equal(expected.PlannedCredit, item.PlannedCredit);
            Assert.Equal(expected.PlannedDebit, item.PlannedDebit);
            Assert.Equal(expected.ActualCredit, item.ActualCredit);
            Assert.Equal(expected.ActualDebit, item.ActualDebit);
        }
    }

    private static async Task ReplaceAllocations(
        HttpClient client,
        Guid budgetId,
        Guid transactionId,
        IReadOnlyList<TransactionAllocationItem> allocations)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}/allocations",
            new ReplaceTransactionAllocationsRequest(allocations));
        response.EnsureSuccessStatusCode();
    }

}

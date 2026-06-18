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

        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/adjustments", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/reallocations", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/periods/{periodId}/allocations", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/budget-lines", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/transactions/{transactionId}/assignments", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/period-summary", paths);
        Assert.DoesNotContain("/api/budgets/{budgetId}/reports/budget-line-trends", paths);
        Assert.Equal(orderedPaths.Order(StringComparer.Ordinal).ToList(), orderedPaths);
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
    public async Task CanonicalAdjustmentsSnapshotAndArchivedHistoryUseLedgerSigns()
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

        Assert.Equal(2_500m, beforeDebit!.BudgetItems.Single(x => x.BudgetItemId == salary.Id).Balance);
        Assert.DoesNotContain(beforeDebit.BudgetItems, x => x.BudgetItemId == groceries.Id && x.Balance != 0);
        Assert.Equal(-500m, afterDebit!.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
        Assert.Equal(10m, afterDebit.BudgetItems.Single(x => x.BudgetItemId == retired.Id).Balance);
        Assert.True(afterDebit.BudgetItems.Single(x => x.BudgetItemId == retired.Id).IsArchived);
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
        Assert.Equal(30m, snapshot!.BudgetItems.Single(x => x.BudgetItemId == dining.Id).Balance);
        Assert.Equal(-30m, snapshot.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
    }

    [Fact]
    public async Task CanonicalDateRangeReportsUseLedgerActivityAndReconciliation()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var salary = await CreateBudgetItem(client, budget.Id, "Salary");
        var groceries = await CreateBudgetItem(client, budget.Id, "Groceries");
        await RecordBudgetItemAdjustment(client, budget.Id, salary.Id, 2_500m, BudgetAdjustmentType.Credit, new DateOnly(2026, 6, 1), "Expected salary");
        await RecordBudgetItemAdjustment(client, budget.Id, groceries.Id, 500m, BudgetAdjustmentType.Debit, new DateOnly(2026, 6, 1), "Expected groceries");
        var pay = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 2_400m, TransactionDirection.Credit);
        var spend = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 16), 120m, TransactionDirection.Debit);
        await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{pay.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(salary.Id, 2_400m)]));
        await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{spend.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 100m)]));

        var activity = await client.GetFromJsonAsync<BudgetActivityReport>(
            $"/api/budgets/{budget.Id}/reports/activity?from=2026-06-01&to=2026-06-30");
        var reconciliation = await client.GetFromJsonAsync<ReconciliationReport>(
            $"/api/budgets/{budget.Id}/reports/reconciliation?from=2026-06-01&to=2026-06-30");
        var csv = await client.GetStringAsync(
            $"/api/budgets/{budget.Id}/reports/activity.csv?from=2026-06-01&to=2026-06-30");

        Assert.Equal(2_500m, activity!.BudgetItems.Single(x => x.BudgetItemId == salary.Id).PlannedCredit);
        Assert.Equal(2_400m, activity.BudgetItems.Single(x => x.BudgetItemId == salary.Id).ActualCredit);
        Assert.Equal(500m, activity.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).PlannedDebit);
        Assert.Equal(100m, activity.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).ActualDebit);
        Assert.Equal(20m, reconciliation!.DebitDifference);
        Assert.Contains("budgetItemId,name,plannedCredit", csv);
    }

    [Fact]
    public async Task ReallocationRejectsMovingMoreThanSourceClosingBalance()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries");
        var dining = await CreateBudgetLine(client, budget.Id, "Dining");
        await ReplaceAllocations(client, budget.Id, period.Id, [
            new BudgetLineAllocationItem(groceries.Id, 100m),
            new BudgetLineAllocationItem(dining.Id, 40m)
        ]);
        var spend = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 10), 70m, TransactionDirection.Debit);
        await ReplaceAssignments(client, budget.Id, spend.Id, [new TransactionAssignmentItem(groceries.Id, 70m)]);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{period.Id}/reallocations",
            new CreateBudgetReallocationRequest(groceries.Id, dining.Id, 30.01m, "Attempt to overdraw source"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var summary = await GetPeriodSummary(client, budget.Id, period.Id);
        var grocerySummary = summary.Lines.Single(x => x.BudgetLineId == groceries.Id);
        Assert.Equal(70m, grocerySummary.ActualAmount);
        Assert.Equal(30m, grocerySummary.ClosingBalance);
        Assert.Equal(0m, grocerySummary.ReallocationOut);
    }

    [Fact]
    public async Task ReallocationAllowsAvailableBalanceWithoutChangingActuals()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries");
        var dining = await CreateBudgetLine(client, budget.Id, "Dining");
        await ReplaceAllocations(client, budget.Id, period.Id, [
            new BudgetLineAllocationItem(groceries.Id, 100m),
            new BudgetLineAllocationItem(dining.Id, 40m)
        ]);
        var spend = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 10), 70m, TransactionDirection.Debit);
        await ReplaceAssignments(client, budget.Id, spend.Id, [new TransactionAssignmentItem(groceries.Id, 70m)]);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{period.Id}/reallocations",
            new CreateBudgetReallocationRequest(groceries.Id, dining.Id, 30m, "Move remaining grocery budget"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var summary = await GetPeriodSummary(client, budget.Id, period.Id);
        var grocerySummary = summary.Lines.Single(x => x.BudgetLineId == groceries.Id);
        var diningSummary = summary.Lines.Single(x => x.BudgetLineId == dining.Id);
        Assert.Equal(70m, grocerySummary.ActualAmount);
        Assert.Equal(0m, diningSummary.ActualAmount);
        Assert.Equal(30m, grocerySummary.ReallocationOut);
        Assert.Equal(30m, diningSummary.ReallocationIn);
        Assert.Equal(0m, grocerySummary.ClosingBalance);
        Assert.Equal(70m, diningSummary.ClosingBalance);
        Assert.Equal(70m, summary.ActualDebit);
    }

    [Fact]
    public async Task CreditTransactionAppearsAsUnassignedUntilAssignedToCreditLine()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions",
            new
            {
                transactionDate = "2026-06-17",
                description = "Visa",
                amount = 3000m,
                direction = 1,
                sourceAccount = "Spending",
                externalReference = "Payment",
                notes = "Earnings"
            });
        response.EnsureSuccessStatusCode();
        var transaction = (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;

        var unassignedSummary = await GetPeriodSummary(client, budget.Id, period.Id);
        Assert.Equal(TransactionDirection.Credit, transaction.Direction);
        Assert.Equal(3000m, unassignedSummary.UnassignedCreditTotal);
        Assert.Equal(0m, unassignedSummary.ActualCredit);

        await ReplaceAssignments(client, budget.Id, transaction.Id, [new TransactionAssignmentItem(salary.Id, 3000m)]);

        var assignedSummary = await GetPeriodSummary(client, budget.Id, period.Id);
        Assert.Equal(0m, assignedSummary.UnassignedCreditTotal);
        Assert.Equal(3000m, assignedSummary.ActualCredit);
        Assert.Equal(3000m, assignedSummary.Lines.Single(x => x.BudgetLineId == salary.Id).ActualAmount);
    }

    [Fact]
    public async Task ArchivedLineAllowsHistoricalAdjustmentOnlyWhenLineHadPeriodActivity()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var historicalPeriod = await CreatePeriod(client, budget.Id);
        var futurePeriod = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var retired = await CreateBudgetLine(client, budget.Id, "Retired category");
        await ReplaceAllocations(client, budget.Id, historicalPeriod.Id, [new BudgetLineAllocationItem(retired.Id, 25m)]);
        await ArchiveBudgetLine(client, budget.Id, retired.Id);

        var historicalResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{historicalPeriod.Id}/adjustments",
            new CreateBudgetAdjustmentRequest(retired.Id, -5m, "Historical correction"));
        var futureResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{futurePeriod.Id}/adjustments",
            new CreateBudgetAdjustmentRequest(retired.Id, -5m, "Future correction should be blocked"));

        Assert.Equal(HttpStatusCode.Created, historicalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, futureResponse.StatusCode);
    }

    [Fact]
    public async Task ArchivedLineAllowsHistoricalAssignmentOnlyWhenLineHadPeriodActivity()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var historicalPeriod = await CreatePeriod(client, budget.Id);
        var futurePeriod = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var retired = await CreateBudgetLine(client, budget.Id, "Retired category");
        await ReplaceAllocations(client, budget.Id, historicalPeriod.Id, [new BudgetLineAllocationItem(retired.Id, 25m)]);
        await ArchiveBudgetLine(client, budget.Id, retired.Id);
        var historicalTransaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 10), 10m, TransactionDirection.Debit);
        var futureTransaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 7, 10), 10m, TransactionDirection.Debit);

        var historicalResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{historicalTransaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(retired.Id, 10m)]));
        var futureResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{futureTransaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(retired.Id, 10m)]));

        Assert.Equal(HttpStatusCode.NoContent, historicalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, futureResponse.StatusCode);
        Assert.NotEqual(historicalPeriod.Id, futurePeriod.Id);
    }

    [Fact]
    public async Task ArchivedLineAllowsHistoricalReallocationOnlyWhenLineHadPeriodActivity()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var historicalPeriod = await CreatePeriod(client, budget.Id);
        var futurePeriod = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var retired = await CreateBudgetLine(client, budget.Id, "Retired category");
        var dining = await CreateBudgetLine(client, budget.Id, "Dining");
        await ReplaceAllocations(client, budget.Id, historicalPeriod.Id, [
            new BudgetLineAllocationItem(retired.Id, 25m),
            new BudgetLineAllocationItem(dining.Id, 10m)
        ]);
        await ReplaceAllocations(client, budget.Id, futurePeriod.Id, [new BudgetLineAllocationItem(dining.Id, 10m)]);
        await ArchiveBudgetLine(client, budget.Id, retired.Id);

        var historicalResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{historicalPeriod.Id}/reallocations",
            new CreateBudgetReallocationRequest(retired.Id, dining.Id, 5m, "Historical correction"));
        var futureResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{futurePeriod.Id}/reallocations",
            new CreateBudgetReallocationRequest(retired.Id, dining.Id, 5m, "Future correction should be blocked"));

        Assert.Equal(HttpStatusCode.Created, historicalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, futureResponse.StatusCode);
    }

    [Fact]
    public async Task ArchivedLineAllowsHistoricalAllocationReplacementOnlyWhenLineHadPeriodActivity()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var historicalPeriod = await CreatePeriod(client, budget.Id);
        var futurePeriod = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var retired = await CreateBudgetLine(client, budget.Id, "Retired category");
        var dining = await CreateBudgetLine(client, budget.Id, "Dining");
        await ReplaceAllocations(client, budget.Id, historicalPeriod.Id, [
            new BudgetLineAllocationItem(retired.Id, 25m),
            new BudgetLineAllocationItem(dining.Id, 10m)
        ]);
        await ReplaceAllocations(client, budget.Id, futurePeriod.Id, [new BudgetLineAllocationItem(dining.Id, 10m)]);
        await ArchiveBudgetLine(client, budget.Id, retired.Id);

        var historicalResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{historicalPeriod.Id}/allocations",
            new ReplaceBudgetLineAllocationsRequest([
                new BudgetLineAllocationItem(retired.Id, 30m),
                new BudgetLineAllocationItem(dining.Id, 10m)
            ]));
        var futureResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{futurePeriod.Id}/allocations",
            new ReplaceBudgetLineAllocationsRequest([
                new BudgetLineAllocationItem(retired.Id, 30m),
                new BudgetLineAllocationItem(dining.Id, 10m)
            ]));

        Assert.Equal(HttpStatusCode.NoContent, historicalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, futureResponse.StatusCode);
    }

    [Fact]
    public async Task AuditTimelineExcludesUnrelatedBudgetLevelEventsAndIncludesPeriodScopedImportRecords()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var june = await CreatePeriod(client, budget.Id);
        var july = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var line = await CreateBudgetLine(client, budget.Id, "Temporary line");
        await ArchiveBudgetLine(client, budget.Id, line.Id);

        var csv = """
date,description,amount,direction,source account,external reference,notes
2026-06-10,June shop,12.00,Debit,Current,JUNE-1,
2026-07-10,July shop,13.00,Debit,Current,JULY-1,
""";

        var previewResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transaction-imports/preview",
            new PreviewTransactionImportRequest("multi-period.csv", csv));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<TransactionImportDetail>())!;
        var commitResponse = await client.PostAsync($"/api/budgets/{budget.Id}/transaction-imports/{preview.Batch.Id}/commit", null);
        commitResponse.EnsureSuccessStatusCode();

        var juneAudit = await GetAuditTimeline(client, budget.Id, june.Id);
        var julyAudit = await GetAuditTimeline(client, budget.Id, july.Id);

        Assert.DoesNotContain(juneAudit, x => x.EventType is "BudgetCreated" or "BudgetLineCreated" or "BudgetLineArchived");
        Assert.DoesNotContain(julyAudit, x => x.EventType is "BudgetCreated" or "BudgetLineCreated" or "BudgetLineArchived");
        Assert.Contains(juneAudit, x => x.EventType == "TransactionImportBatchPreviewed" && x.BudgetPeriodId == june.Id);
        Assert.Contains(juneAudit, x => x.EventType == "TransactionImportBatchCommitted" && x.BudgetPeriodId == june.Id);
        Assert.Contains(julyAudit, x => x.EventType == "TransactionImportBatchPreviewed" && x.BudgetPeriodId == july.Id);
        Assert.Contains(julyAudit, x => x.EventType == "TransactionImportBatchCommitted" && x.BudgetPeriodId == july.Id);
        Assert.DoesNotContain(juneAudit, x => x.BudgetPeriodId == july.Id);
        Assert.DoesNotContain(julyAudit, x => x.BudgetPeriodId == june.Id);
    }

    private static async Task<Budget> CreateBudget(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest("Personal", "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetPeriod> CreatePeriod(HttpClient client, Guid budgetId)
    {
        return await CreatePeriod(client, budgetId, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
    }

    private static async Task<BudgetPeriod> CreatePeriod(
        HttpClient client,
        Guid budgetId,
        string name,
        DateOnly startDate,
        DateOnly endDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods",
            new CreateBudgetPeriodRequest(name, startDate, endDate));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetPeriod>())!;
    }

    private static async Task<BudgetLine> CreateBudgetLine(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetLineDirection direction = BudgetLineDirection.Debit)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-lines",
            new CreateBudgetLineRequest(name, direction, BudgetLineRolloverType.PeriodReset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetLine>())!;
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

    private static async Task ArchiveBudgetLine(HttpClient client, Guid budgetId, Guid budgetLineId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-lines/{budgetLineId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task ReplaceAllocations(
        HttpClient client,
        Guid budgetId,
        Guid periodId,
        IReadOnlyList<BudgetLineAllocationItem> allocations)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/periods/{periodId}/allocations",
            new ReplaceBudgetLineAllocationsRequest(allocations));
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

    private static async Task ReplaceAssignments(
        HttpClient client,
        Guid budgetId,
        Guid transactionId,
        IReadOnlyList<TransactionAssignmentItem> assignments)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}/assignments",
            new ReplaceTransactionAssignmentsRequest(assignments));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<PeriodSummary> GetPeriodSummary(HttpClient client, Guid budgetId, Guid periodId)
    {
        return (await client.GetFromJsonAsync<PeriodSummary>(
            $"/api/budgets/{budgetId}/reports/period-summary?periodId={periodId}"))!;
    }

    private static async Task<IReadOnlyList<AuditTimelineItem>> GetAuditTimeline(HttpClient client, Guid budgetId, Guid periodId)
    {
        return (await client.GetFromJsonAsync<IReadOnlyList<AuditTimelineItem>>(
            $"/api/budgets/{budgetId}/reports/audit-timeline?periodId={periodId}"))!;
    }
}

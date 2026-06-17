using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Infrastructure.Persistence;
using BudgetyTzar.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class Phase1ApiIntegrationTests
{
    [Fact]
    public async Task OppositeDirectionTransactionAssignmentIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var refund = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Credit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{refund.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 25m)]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task EmptyTransactionAssignmentReplacementIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var transaction = await CreateTransaction(client, budget.Id, 15m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await app.CountAssignmentsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAssignmentsCannotExceedTransactionAmount()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransactionEditWritesAuditAndPreservesAssignments()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                new DateOnly(2026, 6, 11),
                "Edited groceries",
                30m,
                TransactionDirection.Debit,
                "Current account",
                "EDIT-1",
                "Updated note"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var edited = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(transaction.Id, edited!.Id);
        Assert.Equal("Edited groceries", edited.Description);
        Assert.Equal(30m, edited.Amount);
        Assert.Equal(1, await app.CountAssignmentsAsync(transaction.Id));

        var audit = await client.GetFromJsonAsync<IReadOnlyList<AuditTimelineItem>>(
            $"/api/budgets/{budget.Id}/reports/audit-timeline?periodId={period.Id}");
        var editAudit = audit!.Single(x => x.EventType == "TransactionEdited");
        Assert.NotEqual(Guid.Empty, editAudit.AuditEventId);
        Assert.Equal(nameof(FinancialTransaction), editAudit.EntityType);
        Assert.Equal(transaction.Id, editAudit.EntityId);
        Assert.Equal(period.Id, editAudit.BudgetPeriodId);
    }

    [Fact]
    public async Task TransactionAmountCannotBeEditedBelowCurrentAssignmentTotal()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}",
            new UpdateTransactionRequest(
                transaction.TransactionDate,
                transaction.Description,
                19.99m,
                transaction.Direction,
                transaction.SourceAccount,
                transaction.ExternalReference,
                transaction.Notes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await app.CountAssignmentsAsync(transaction.Id));
        var persisted = await app.GetTransactionAsync(transaction.Id);
        Assert.Equal(25m, persisted!.Amount);
    }

    [Fact]
    public async Task ReallocationsCannotUseCreditBudgetLines()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{period.Id}/reallocations",
            new CreateBudgetReallocationRequest(groceries.Id, salary.Id, 10m, "Invalid credit line target"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BudgetPeriodsCannotOverlapWithinBudget()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        await CreatePeriod(client, budget.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest("Overlap", new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 14)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBudgetPeriodCanSeedInlineAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);

        var period = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest(
                "July 2026",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31),
                [
                    new BudgetLineAllocationItem(groceries.Id, 250m),
                    new BudgetLineAllocationItem(salary.Id, 2_500m)
                ]));

        var allocations = await app.GetAllocationsAsync(period.Id);
        Assert.Equal(2, allocations.Count);
        Assert.Contains(allocations, x => x.BudgetLineId == groceries.Id && x.Amount == 250m);
        Assert.Contains(allocations, x => x.BudgetLineId == salary.Id && x.Amount == 2_500m);
    }

    [Fact]
    public async Task CreateBudgetPeriodCanCopyAllocationsFromPriorPeriod()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var source = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest("May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));
        await ReplaceAllocations(client, budget.Id, source.Id, [new BudgetLineAllocationItem(groceries.Id, 125m)]);

        var copied = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest(
                "June 2026",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                CopyAllocationsFromPeriodId: source.Id));

        var allocations = await app.GetAllocationsAsync(copied.Id);
        var allocation = Assert.Single(allocations);
        Assert.Equal(groceries.Id, allocation.BudgetLineId);
        Assert.Equal(125m, allocation.Amount);
    }

    [Fact]
    public async Task CreateBudgetPeriodCopySkipsArchivedBudgetLines()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var retired = await CreateBudgetLine(client, budget.Id, "Old category", BudgetLineDirection.Debit);
        var source = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest("May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));
        await ReplaceAllocations(
            client,
            budget.Id,
            source.Id,
            [new BudgetLineAllocationItem(groceries.Id, 100m), new BudgetLineAllocationItem(retired.Id, 50m)]);
        await ArchiveBudgetLine(client, budget.Id, retired.Id);

        var copied = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest(
                "June 2026",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                CopyAllocationsFromPeriodId: source.Id));

        var allocation = Assert.Single(await app.GetAllocationsAsync(copied.Id));
        Assert.Equal(groceries.Id, allocation.BudgetLineId);
        Assert.Equal(100m, allocation.Amount);
    }

    [Fact]
    public async Task CreateBudgetPeriodRejectsDuplicateInlineAllocationLines()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest(
                "July 2026",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31),
                [
                    new BudgetLineAllocationItem(groceries.Id, 100m),
                    new BudgetLineAllocationItem(groceries.Id, 25m)
                ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBudgetPeriodRejectsInlineAndCopyAllocationSourcesTogether()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var source = await CreatePeriod(
            client,
            budget.Id,
            new CreateBudgetPeriodRequest("May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest(
                "June 2026",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                [new BudgetLineAllocationItem(groceries.Id, 100m)],
                source.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBudgetPeriodRejectsCopySourceFromAnotherBudget()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client, "Personal");
        var otherBudget = await CreateBudget(client, "Business");
        var otherSource = await CreatePeriod(
            client,
            otherBudget.Id,
            new CreateBudgetPeriodRequest("May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest(
                "June 2026",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                CopyAllocationsFromPeriodId: otherSource.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuditTimelinePreservesAssignmentReplacementAndClearing()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, 25m, TransactionDirection.Debit);

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 25m)]));
        var clearResponse = await client.DeleteAsync($"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments");

        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        var audit = await client.GetFromJsonAsync<IReadOnlyList<AuditTimelineItem>>(
            $"/api/budgets/{budget.Id}/reports/audit-timeline?periodId={period.Id}");

        Assert.Contains(audit!, x => x.EventType == "TransactionAssigned");
        Assert.Contains(audit!, x => x.EventType == "TransactionAssignmentsCleared");
        Assert.All(
            audit!.Where(x => x.EventType is "TransactionAssigned" or "TransactionAssignmentsCleared"),
            x =>
            {
                Assert.NotEqual(Guid.Empty, x.AuditEventId);
                Assert.Equal(nameof(FinancialTransaction), x.EntityType);
                Assert.Equal(transaction.Id, x.EntityId);
                Assert.Equal(period.Id, x.BudgetPeriodId);
            });
    }

    [Fact]
    public async Task AdjustmentEndpointUpdatesPeriodSummary()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        await ReplaceAllocations(client, budget.Id, period.Id, [new BudgetLineAllocationItem(groceries.Id, 100m)]);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods/{period.Id}/adjustments",
            new CreateBudgetAdjustmentRequest(groceries.Id, 12m, "Opening correction"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var summary = await client.GetFromJsonAsync<PeriodSummary>(
            $"/api/budgets/{budget.Id}/reports/period-summary?periodId={period.Id}");

        Assert.Equal(12m, summary!.Lines.Single(x => x.BudgetLineId == groceries.Id).AdjustmentAmount);
        Assert.Equal(112m, summary.Lines.Single(x => x.BudgetLineId == groceries.Id).ClosingBalance);
    }

    [Fact]
    public async Task TransactionImportPreviewCommitAndRecommitAreIdempotent()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        await CreatePeriod(client, budget.Id);
        await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions",
            new CreateTransactionRequest(
                new DateOnly(2026, 6, 9),
                "Existing shop",
                12.34m,
                TransactionDirection.Debit,
                "Current account",
                "EXT-1",
                null));
        var csv = """
date,description,amount,direction,source account,external reference,notes
2026-06-09,Existing shop,12.34,Debit,Current account,EXT-1,
2026-06-10,Salary,2500.00,Credit,Current account,EXT-2,June pay
""";

        var previewResponse = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/transaction-imports/preview",
            new PreviewTransactionImportRequest("transactions.csv", csv));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<TransactionImportDetail>())!;

        Assert.Equal(2, preview.Rows.Count);
        Assert.Contains(preview.Rows, x => x.IsDuplicateCandidate);

        var commitResponse = await client.PostAsync($"/api/budgets/{budget.Id}/transaction-imports/{preview.Batch.Id}/commit", null);
        var recommitResponse = await client.PostAsync($"/api/budgets/{budget.Id}/transaction-imports/{preview.Batch.Id}/commit", null);

        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recommitResponse.StatusCode);
        Assert.Equal(3, await app.CountTransactionsAsync(budget.Id));
    }

    [Fact]
    public async Task ReconciliationTrendCreditVarianceAndCsvReportsAreAvailable()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);
        await ReplaceAllocations(
            client,
            budget.Id,
            period.Id,
            [new BudgetLineAllocationItem(groceries.Id, 100m), new BudgetLineAllocationItem(salary.Id, 2_500m)]);
        var spend = await CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);
        var pay = await CreateTransaction(client, budget.Id, 2_500m, TransactionDirection.Credit);
        await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{spend.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 20m)]));
        await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{pay.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(salary.Id, 2_500m)]));

        var reconciliation = await client.GetAsync($"/api/budgets/{budget.Id}/reports/reconciliation?periodId={period.Id}");
        var trends = await client.GetFromJsonAsync<IReadOnlyList<BudgetLineTrendItem>>(
            $"/api/budgets/{budget.Id}/reports/budget-line-trends?budgetLineId={groceries.Id}&from=2026-06-01&to=2026-06-30");
        var creditVariance = await client.GetFromJsonAsync<IReadOnlyList<CreditVarianceItem>>(
            $"/api/budgets/{budget.Id}/reports/credit-variance?from=2026-06-01&to=2026-06-30");
        var csv = await client.GetStringAsync($"/api/budgets/{budget.Id}/reports/period-summary.csv?periodId={period.Id}");

        Assert.Equal(HttpStatusCode.OK, reconciliation.StatusCode);
        Assert.Single(trends!);
        var creditVarianceItem = Assert.Single(creditVariance!);
        Assert.Equal(0m, creditVarianceItem.CreditVariance);
        Assert.Equal(salary.Id, creditVarianceItem.BudgetLineId);
        Assert.Equal("Salary", creditVarianceItem.BudgetLineName);
        Assert.Contains("budgetLineId,name,direction", csv);
        Assert.Contains("Groceries", csv);
    }

    [Fact]
    public async Task ReconciliationReportsDebitAndCreditDifferences()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);
        var spend = await CreateTransaction(client, budget.Id, 100m, TransactionDirection.Debit);
        var pay = await CreateTransaction(client, budget.Id, 200m, TransactionDirection.Credit);
        var debitAssignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{spend.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 40m)]));
        var creditAssignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{pay.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(salary.Id, 150m)]));
        debitAssignResponse.EnsureSuccessStatusCode();
        creditAssignResponse.EnsureSuccessStatusCode();

        var report = await client.GetFromJsonAsync<ReconciliationReport>(
            $"/api/budgets/{budget.Id}/reports/reconciliation?periodId={period.Id}");

        Assert.Equal(60m, report!.DebitDifference);
        Assert.Equal(50m, report.CreditDifference);
    }

    [Fact]
    public async Task CreditVarianceReturnsOneItemPerCreditBudgetLinePerPeriod()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id);
        var salary = await CreateBudgetLine(client, budget.Id, "Salary", BudgetLineDirection.Credit);
        var bonus = await CreateBudgetLine(client, budget.Id, "Bonus", BudgetLineDirection.Credit);
        await ReplaceAllocations(
            client,
            budget.Id,
            period.Id,
            [new BudgetLineAllocationItem(salary.Id, 2_500m), new BudgetLineAllocationItem(bonus.Id, 500m)]);
        var pay = await CreateTransaction(client, budget.Id, 2_400m, TransactionDirection.Credit);
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{pay.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(salary.Id, 2_400m)]));
        assignResponse.EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<IReadOnlyList<CreditVarianceItem>>(
            $"/api/budgets/{budget.Id}/reports/credit-variance?from=2026-06-01&to=2026-06-30");

        Assert.Equal(2, items!.Count);
        Assert.Contains(items, x => x.BudgetLineId == salary.Id && x.BudgetLineName == "Salary" && x.PlannedCredit == 2_500m && x.ActualCredit == 2_400m && x.CreditVariance == -100m);
        Assert.Contains(items, x => x.BudgetLineId == bonus.Id && x.BudgetLineName == "Bonus" && x.PlannedCredit == 500m && x.ActualCredit == 0m && x.CreditVariance == -500m);
    }

    private static async Task<Budget> CreateBudget(HttpClient client, string name = "Personal")
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(name, "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetPeriod> CreatePeriod(HttpClient client, Guid budgetId)
    {
        return await CreatePeriod(
            client,
            budgetId,
            new CreateBudgetPeriodRequest("June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
    }

    private static async Task<BudgetPeriod> CreatePeriod(
        HttpClient client,
        Guid budgetId,
        CreateBudgetPeriodRequest request)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/periods",
            request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetPeriod>())!;
    }

    private static async Task<BudgetLine> CreateBudgetLine(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-lines",
            new CreateBudgetLineRequest(name, direction, BudgetLineRolloverType.PeriodReset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetLine>())!;
    }

    private static async Task ArchiveBudgetLine(HttpClient client, Guid budgetId, Guid lineId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-lines/{lineId}/archive", null);
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
        decimal amount,
        TransactionDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(
                new DateOnly(2026, 6, 10),
                $"{direction} transaction",
                amount,
                direction,
                "Current account",
                null,
                null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }
}

internal sealed class BudgetApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BudgetDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<BudgetDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BudgetDbContext>>();
            services.RemoveAll<DbConnection>();

            services.AddSingleton<DbConnection>(_ =>
            {
                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();
                return connection;
            });

            services.AddDbContext<BudgetDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<int> CountAssignmentsAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.TransactionAssignments.CountAsync(x => x.TransactionId == transactionId);
    }

    public async Task<int> CountTransactionsAsync(Guid budgetId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.Transactions.CountAsync(x => x.BudgetId == budgetId);
    }

    public async Task<FinancialTransaction?> GetTransactionAsync(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == transactionId);
    }

    public async Task<IReadOnlyList<BudgetLineAllocation>> GetAllocationsAsync(Guid periodId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        return await db.BudgetLineAllocations
            .AsNoTracking()
            .Where(x => x.BudgetPeriodId == periodId)
            .OrderBy(x => x.Id)
            .ToListAsync();
    }
}

using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class PostgresPhase1ApiIntegrationTests
{
    [Fact]
    public async Task ApiStartsAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MonetaryValuesPersistWithTwoDecimalPrecision()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await CreateBudget(client);
        var period = await CreatePeriod(client, budget.Id, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        await ReplaceAllocations(client, budget.Id, period.Id, [new BudgetLineAllocationItem(groceries.Id, 250.25m)]);
        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 1234.56m);

        var assignmentResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/assignments",
            new ReplaceTransactionAssignmentsRequest([new TransactionAssignmentItem(groceries.Id, 78.90m)]));
        assignmentResponse.EnsureSuccessStatusCode();

        Assert.Equal(1234.56m, (await app.GetTransactionAsync(transaction.Id))!.Amount);
        Assert.Equal(250.25m, (await app.GetAllocationAsync(period.Id, groceries.Id))!.Amount);
        Assert.Equal(78.90m, (await app.GetAssignmentAsync(transaction.Id, groceries.Id))!.Amount);
    }

    [Fact]
    public async Task PeriodAndDateFilteringWorksAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await CreateBudget(client);
        var june = await CreatePeriod(client, budget.Id, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var july = await CreatePeriod(client, budget.Id, "July 2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var juneTransaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 12.34m, "June shop");
        await CreateTransaction(client, budget.Id, new DateOnly(2026, 7, 5), 45.67m, "July shop");

        var periodForDate = await client.GetFromJsonAsync<BudgetPeriod>(
            $"/api/budgets/{budget.Id}/periods/for-date?date=2026-06-15");
        var transactionsByPeriod = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?periodId={june.Id}");
        var transactionsByDateRange = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?from=2026-07-01&to=2026-07-31");

        Assert.Equal(june.Id, periodForDate!.Id);
        Assert.NotNull(transactionsByPeriod);
        Assert.DoesNotContain(transactionsByPeriod, x => x.TransactionDate < june.StartDate || x.TransactionDate > june.EndDate);
        Assert.Contains(transactionsByPeriod, x => x.Id == juneTransaction.Id);
        Assert.Single(transactionsByDateRange!);
        Assert.Equal(july.Id, (await client.GetFromJsonAsync<BudgetPeriod>(
            $"/api/budgets/{budget.Id}/periods/for-date?date=2026-07-05"))!.Id);
    }

    [Fact]
    public async Task OverlapConstraintBehavesAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await CreateBudget(client);
        await CreatePeriod(client, budget.Id, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var overlappingPeriod = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/periods",
            new CreateBudgetPeriodRequest("Overlap", new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 14)));

        Assert.Equal(HttpStatusCode.BadRequest, overlappingPeriod.StatusCode);
    }

    [Fact]
    public async Task UniqueBudgetLineNameConstraintBehavesAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await CreateBudget(client);
        await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);

        var duplicateLine = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-lines",
            new CreateBudgetLineRequest("Groceries", BudgetLineDirection.Debit, BudgetLineRolloverType.PeriodReset));

        Assert.Equal(HttpStatusCode.BadRequest, duplicateLine.StatusCode);
    }

    private static async Task<Budget> CreateBudget(HttpClient client, string name = "Personal")
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(name, "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
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
        BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-lines",
            new CreateBudgetLineRequest(name, direction, BudgetLineRolloverType.PeriodReset));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetLine>())!;
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
        DateOnly date,
        decimal amount,
        string description = "PostgreSQL transaction")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(
                date,
                description,
                amount,
                TransactionDirection.Debit,
                "Current account",
                null,
                null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }
}

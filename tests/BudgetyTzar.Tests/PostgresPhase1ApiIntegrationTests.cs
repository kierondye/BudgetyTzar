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
        var groceries = await CreateBudgetLine(client, budget.Id, "Groceries", BudgetLineDirection.Debit);
        var transaction = await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 1234.56m);

        var assignmentResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAssignmentItem(groceries.Id, 78.90m)]));
        assignmentResponse.EnsureSuccessStatusCode();

        Assert.Equal(1234.56m, (await app.GetTransactionAsync(transaction.Id))!.Amount);
        Assert.Equal(78.90m, (await app.GetAssignmentAsync(transaction.Id, groceries.Id))!.Amount);
    }

    [Fact]
    public async Task DateFilteringWorksAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await CreateBudget(client);
        await CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 12.34m, "June shop");
        await CreateTransaction(client, budget.Id, new DateOnly(2026, 7, 5), 45.67m, "July shop");

        var transactionsByDateRange = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?from=2026-07-01&to=2026-07-31");

        Assert.Single(transactionsByDateRange!);
        Assert.Equal("July shop", transactionsByDateRange![0].Description);
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
            $"/api/budgets/{budget.Id}/budget-items",
            new CreateBudgetItemRequest("Groceries"));

        Assert.Equal(HttpStatusCode.BadRequest, duplicateLine.StatusCode);
    }

    private static async Task<Budget> CreateBudget(HttpClient client, string name = "Personal")
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(name, "GBP"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    private static async Task<BudgetItemDto> CreateBudgetLine(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetLineDirection direction)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetItemDto>())!;
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

using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class PostgresCompatibilityTests
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
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 15), 1234.56m, TransactionDirection.Debit, "PostgreSQL transaction");

        var allocationResponse = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(groceries.Id, 78.90m)]));
        allocationResponse.EnsureSuccessStatusCode();

        Assert.Equal(1234.56m, (await app.GetTransactionAsync(transaction.Id))!.Amount);
        Assert.Equal(78.90m, (await app.GetAllocationAsync(transaction.Id, groceries.Id))!.Amount);
    }

    [Fact]
    public async Task DateFilteringWorksAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 12.34m, TransactionDirection.Debit, "June shop");
        await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 7, 5), 45.67m, TransactionDirection.Debit, "July shop");

        var transactionsByDateRange = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?from=2026-07-01&to=2026-07-31");

        Assert.Single(transactionsByDateRange!);
        Assert.Equal("July shop", transactionsByDateRange![0].Description);
    }

    [Fact]
    public async Task UniqueBudgetItemNameConstraintBehavesAgainstPostgreSql()
    {
        await using var app = await PostgresBudgetApiFactory.StartAsync();
        await app.ResetDatabaseAsync();
        var client = app.CreateClient();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");

        var duplicateLine = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items",
            new CreateBudgetItemRequest("Groceries"));

        Assert.Equal(HttpStatusCode.BadRequest, duplicateLine.StatusCode);
    }
}

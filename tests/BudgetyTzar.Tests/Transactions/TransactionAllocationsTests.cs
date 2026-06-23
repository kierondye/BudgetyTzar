using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Transactions;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class TransactionAllocationsTests
{
    [Fact]
    public async Task OppositeDirectionTransactionAllocationIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var refund = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 25m, TransactionDirection.Credit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{refund.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(groceries.Id, 25m)]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task EmptyTransactionAllocationReplacementIsAccepted()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 15m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await app.CountAllocationsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAllocationsCannotExceedTransactionAmount()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransactionAllocationAliasCanClearToEmpty()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 20m)]);

        var clearResponse = await client.DeleteAsync($"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");
        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAllocation>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        Assert.Empty(allocations!);
        Assert.Equal(0, await app.CountAllocationsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAllocationAliasCanReplaceWithSplitAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var household = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Household");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 50m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([
                new TransactionAllocationItem(groceries.Id, 30m),
                new TransactionAllocationItem(household.Id, 15m)
            ]));
        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAllocation>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, allocations!.Count);
        Assert.Contains(allocations, x => x.BudgetItemId == groceries.Id && x.Amount == 30m);
        Assert.Contains(allocations, x => x.BudgetItemId == household.Id && x.Amount == 15m);
    }

    [Fact]
    public async Task TransactionAllocationNotesAreStoredTrimmedAndReturned()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 50m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([
                new TransactionAllocationItem(groceries.Id, 30m, "  Weekly shop  ")
            ]));
        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAllocation>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var allocation = Assert.Single(allocations!);
        Assert.Equal("Weekly shop", allocation.Notes);
    }

    [Fact]
    public async Task TransactionAllocationNotesValidateMaximumLength()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 50m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([
                new TransactionAllocationItem(groceries.Id, 30m, new string('x', 501))
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransactionAllocationAliasRejectsOverAllocation()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(groceries.Id, 20.01m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await app.CountAllocationsAsync(transaction.Id));
    }

    [Fact]
    public async Task TransactionAllocationAliasCanGetExistingAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, 20m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, transaction.Id, [new TransactionAllocationItem(groceries.Id, 12m)]);

        var allocations = await client.GetFromJsonAsync<IReadOnlyList<TransactionAllocation>>(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations");

        var allocation = Assert.Single(allocations!);
        Assert.Equal(groceries.Id, allocation.BudgetItemId);
        Assert.Equal(12m, allocation.Amount);
    }
}

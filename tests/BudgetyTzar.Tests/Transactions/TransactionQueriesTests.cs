using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class TransactionQueriesTests
{
    [Fact]
    public async Task AllocationStatusQueryFiltersTransactionList()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries", BudgetItemKind.Consumption);
        var unallocated = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 20), 10m, TransactionDirection.Debit);
        var allocated = await BudgetApiTestClient.CreateTransaction(client, budget.Id, new DateOnly(2026, 6, 21), 10m, TransactionDirection.Debit);
        await BudgetApiTestClient.ReplaceAllocations(client, budget.Id, allocated.Id, [new TransactionAllocationItem(groceries.Id, 10m)]);

        var transactions = await client.GetFromJsonAsync<IReadOnlyList<FinancialTransaction>>(
            $"/api/budgets/{budget.Id}/transactions?allocationStatus=unallocated");

        var transaction = Assert.Single(transactions!);
        Assert.Equal(unallocated.Id, transaction.Id);
    }
}

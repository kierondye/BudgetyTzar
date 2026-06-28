using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

internal static class BudgetApiTestClient
{
    public static async Task<Budget> CreateBudget(HttpClient client, string name = "Personal", string currency = "GBP")
    {
        var response = await client.PostAsJsonAsync("/api/budgets", new CreateBudgetRequest(name, currency));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Budget>())!;
    }

    public static async Task<BudgetItemDto> CreateBudgetItem(
        HttpClient client,
        Guid budgetId,
        string name,
        BudgetItemKind kind)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name, kind));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetItemDto>())!;
    }

    public static async Task ArchiveBudgetItem(HttpClient client, Guid budgetId, Guid budgetItemId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/budget-items/{budgetItemId}/archive", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<FinancialTransaction> CreateTransaction(
        HttpClient client,
        Guid budgetId,
        DateOnly date,
        decimal amount,
        TransactionDirection direction,
        string description = "Groceries")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions",
            new CreateTransactionRequest(
                date,
                description,
                amount,
                direction,
                "Current account",
                null,
                null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FinancialTransaction>())!;
    }

    public static Task<FinancialTransaction> CreateTransaction(
        HttpClient client,
        Guid budgetId,
        decimal amount,
        TransactionDirection direction)
    {
        return CreateTransaction(client, budgetId, new DateOnly(2026, 6, 10), amount, direction, $"{direction} transaction");
    }

    public static async Task UpdateTransaction(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budgetId}/transactions/{transactionId}",
            new UpdateTransactionRequest(new DateOnly(2026, 6, 13), "Groceries updated", 35m, TransactionDirection.Debit, "Current", null, null));
        response.EnsureSuccessStatusCode();
    }

    public static async Task IgnoreTransaction(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.PostAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/ignore", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task ReplaceAllocations(
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

    public static async Task ClearAllocations(HttpClient client, Guid budgetId, Guid transactionId)
    {
        var response = await client.DeleteAsync($"/api/budgets/{budgetId}/transactions/{transactionId}/allocations");
        response.EnsureSuccessStatusCode();
    }

    public static async Task RecordAdjustment(
        HttpClient client,
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes = "Test adjustment")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments",
            new CreateBudgetItemAdjustmentRequest(amount, type, date, notes));
        response.EnsureSuccessStatusCode();
    }

    public static async Task RecordReallocation(
        HttpClient client,
        Guid budgetId,
        Guid fromBudgetItemId,
        Guid toBudgetItemId,
        decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 12),
                "Schema validation reallocation",
                [
                    new BudgetReallocationAdjustmentItem(fromBudgetItemId, amount, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(toBudgetItemId, amount, BudgetAdjustmentType.Debit)
                ]));
        response.EnsureSuccessStatusCode();
    }

    public static async Task<BudgetSnapshot> GetSnapshot(HttpClient client, Guid budgetId, DateOnly date)
    {
        return (await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budgetId}/snapshot?date={date:yyyy-MM-dd}"))!;
    }

    public static void AssertSnapshot(
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
}

using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests.Reporting;

public sealed class BudgetSummaryApiTests
{
    [Fact]
    public async Task Get_budget_summary_returns_empty_summary_for_budget_without_items()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        Assert.Equal(budget.BudgetId, summary.BudgetId);
        Assert.Equal("UK", summary.Name);
        Assert.Equal("GBP", summary.Currency);
        Assert.Empty(summary.Funding.Items);
        Assert.Empty(summary.Consumption.Items);
        AssertSectionTotals("0.00", "0.00", "0.00", summary.Funding);
        AssertSectionTotals("0.00", "0.00", "0.00", summary.Consumption);
        Assert.Equal("0.00", summary.Overall.PlannedSurplus);
        Assert.Equal("0.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_shows_planned_deficit()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "1000.00");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Rent", "Consumption", "1200.00");

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        Assert.Equal("-200.00", summary.Overall.PlannedSurplus);
        Assert.Equal("0.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_calculates_allocated_funding_actuals()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");
        var salaryPayment = await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var fundingCorrection = await CreateTransactionAsync(server, "Salary correction", "Debit", "2026-07-02", "100.00", "GBP");

        await AllocateTransactionAsync(server, salaryPayment.TransactionId, salary.BudgetItemId);
        await AllocateTransactionAsync(server, fundingCorrection.TransactionId, salary.BudgetItemId);

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        var item = Assert.Single(summary.Funding.Items);
        Assert.Equal(salary.BudgetItemId, item.BudgetItemId);
        Assert.Equal("Salary", item.Name);
        Assert.Equal("3000.00", item.PlannedAmount);
        Assert.Equal("2900.00", item.ActualAmount);
        Assert.Equal("100.00", item.RemainingAmount);
        AssertSectionTotals("3000.00", "2900.00", "100.00", summary.Funding);
        Assert.Equal("2900.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_calculates_allocated_consumption_refunds()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var shop = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "200.00", "GBP");
        var refund = await CreateTransactionAsync(server, "Refund", "Credit", "2026-07-03", "20.00", "GBP");

        await AllocateTransactionAsync(server, shop.TransactionId, groceries.BudgetItemId);
        await AllocateTransactionAsync(server, refund.TransactionId, groceries.BudgetItemId);

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        var item = Assert.Single(summary.Consumption.Items);
        Assert.Equal(groceries.BudgetItemId, item.BudgetItemId);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal("400.00", item.PlannedAmount);
        Assert.Equal("180.00", item.ActualAmount);
        Assert.Equal("220.00", item.RemainingAmount);
        AssertSectionTotals("400.00", "180.00", "220.00", summary.Consumption);
        Assert.Equal("-180.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_ignores_unallocated_transactions()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        await CreateTransactionAsync(server, "Unallocated salary", "Credit", "2026-07-01", "3000.00", "GBP");
        await CreateTransactionAsync(server, "Unallocated groceries", "Debit", "2026-07-02", "200.00", "GBP");

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        Assert.Equal("0.00", Assert.Single(summary.Funding.Items).ActualAmount);
        Assert.Equal("0.00", Assert.Single(summary.Consumption.Items).ActualAmount);
        AssertSectionTotals("3000.00", "0.00", "3000.00", summary.Funding);
        AssertSectionTotals("400.00", "0.00", "400.00", summary.Consumption);
        Assert.Equal("2600.00", summary.Overall.PlannedSurplus);
        Assert.Equal("0.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_is_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(server.Client, "User A", "GBP");
        var userAItem = await CreateBudgetItemAsync(server.Client, userABudget.BudgetId, "Salary", "Funding", "3000.00");
        var userATransaction = await CreateTransactionAsync(server.Client, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        await AllocateTransactionAsync(server.Client, userATransaction.TransactionId, userAItem.BudgetItemId);
        var userBBudget = await CreateBudgetAsync(userB, "User B", "GBP");
        await CreateBudgetItemAsync(userB, userBBudget.BudgetId, "Salary", "Funding", "1000.00");

        using var userBGetsUserASummary = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}/summary");
        var userASummary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{userABudget.BudgetId}/summary");

        Assert.Equal(HttpStatusCode.NotFound, userBGetsUserASummary.StatusCode);
        Assert.NotNull(userASummary);
        Assert.Equal("3000.00", userASummary.Funding.TotalPlannedAmount);
        Assert.Equal("3000.00", userASummary.Funding.TotalActualAmount);
    }

    [Fact]
    public async Task Get_budget_summary_allows_negative_actuals_and_over_plan_amounts()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var reversal = await CreateBudgetItemAsync(server, budget.BudgetId, "Reversal", "Funding", "50.00");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "100.00");
        var fundingDebit = await CreateTransactionAsync(server, "Funding reversal", "Debit", "2026-07-04", "75.00", "GBP");
        var overspend = await CreateTransactionAsync(server, "Overspend", "Debit", "2026-07-05", "125.00", "GBP");

        await AllocateTransactionAsync(server, fundingDebit.TransactionId, reversal.BudgetItemId);
        await AllocateTransactionAsync(server, overspend.TransactionId, groceries.BudgetItemId);

        var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.NotNull(summary);
        var funding = Assert.Single(summary.Funding.Items);
        var consumption = Assert.Single(summary.Consumption.Items);

        Assert.Equal("-75.00", funding.ActualAmount);
        Assert.Equal("125.00", funding.RemainingAmount);
        Assert.Equal("125.00", consumption.ActualAmount);
        Assert.Equal("-25.00", consumption.RemainingAmount);
        AssertSectionTotals("50.00", "-75.00", "125.00", summary.Funding);
        AssertSectionTotals("100.00", "125.00", "-25.00", summary.Consumption);
        Assert.Equal("-200.00", summary.Overall.ActualSurplus);
    }

    [Fact]
    public async Task Get_budget_summary_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync($"/api/budgets/{Guid.NewGuid()}/summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void AssertSectionTotals(
        string plannedAmount,
        string actualAmount,
        string remainingAmount,
        BudgetSummarySectionResponse section)
    {
        Assert.Equal(plannedAmount, section.TotalPlannedAmount);
        Assert.Equal(actualAmount, section.TotalActualAmount);
        Assert.Equal(remainingAmount, section.TotalRemainingAmount);
    }

    private static async Task<BudgetResponse> CreateBudgetAsync(TestApiServer server, string name, string currency)
    {
        return await CreateBudgetAsync(server.Client, name, currency);
    }

    private static async Task<BudgetResponse> CreateBudgetAsync(HttpClient client, string name, string currency)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest(name, currency));

        response.EnsureSuccessStatusCode();

        var budget = await response.Content.ReadFromJsonAsync<BudgetResponse>();
        return budget ?? throw new InvalidOperationException("Create budget response was empty.");
    }

    private static async Task<BudgetItemResponse> CreateBudgetItemAsync(
        TestApiServer server,
        Guid budgetId,
        string name,
        string kind,
        string plannedAmount)
    {
        return await CreateBudgetItemAsync(server.Client, budgetId, name, kind, plannedAmount);
    }

    private static async Task<BudgetItemResponse> CreateBudgetItemAsync(
        HttpClient client,
        Guid budgetId,
        string name,
        string kind,
        string plannedAmount)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name, kind, plannedAmount));

        response.EnsureSuccessStatusCode();

        var budgetItem = await response.Content.ReadFromJsonAsync<BudgetItemResponse>();
        return budgetItem ?? throw new InvalidOperationException("Create budget item response was empty.");
    }

    private static async Task<TransactionResponse> CreateTransactionAsync(
        TestApiServer server,
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        return await CreateTransactionAsync(server.Client, description, type, transactionDate, amount, currency);
    }

    private static async Task<TransactionResponse> CreateTransactionAsync(
        HttpClient client,
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest(description, type, transactionDate, amount, currency));

        response.EnsureSuccessStatusCode();

        var transaction = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        return transaction ?? throw new InvalidOperationException("Create transaction response was empty.");
    }

    private static async Task AllocateTransactionAsync(TestApiServer server, Guid transactionId, Guid budgetItemId)
    {
        await AllocateTransactionAsync(server.Client, transactionId, budgetItemId);
    }

    private static async Task AllocateTransactionAsync(HttpClient client, Guid transactionId, Guid budgetItemId)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));

        response.EnsureSuccessStatusCode();
    }

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);

    private sealed record BudgetResponse(Guid BudgetId, string Name, string Currency);

    private sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);

    private sealed record TransactionResponse(Guid TransactionId);

    private sealed record BudgetSummaryResponse(
        Guid BudgetId,
        string Name,
        string Currency,
        BudgetSummarySectionResponse Funding,
        BudgetSummarySectionResponse Consumption,
        BudgetSummaryOverallResponse Overall);

    private sealed record BudgetSummarySectionResponse(
        IReadOnlyList<BudgetSummaryItemResponse> Items,
        string TotalPlannedAmount,
        string TotalActualAmount,
        string TotalRemainingAmount);

    private sealed record BudgetSummaryItemResponse(
        Guid BudgetItemId,
        string Name,
        string PlannedAmount,
        string ActualAmount,
        string RemainingAmount);

    private sealed record BudgetSummaryOverallResponse(string PlannedSurplus, string ActualSurplus);
}

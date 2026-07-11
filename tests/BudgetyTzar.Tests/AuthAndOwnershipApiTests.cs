using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests;

public sealed class AuthAndOwnershipApiTests
{
    [Fact]
    public async Task Business_api_requests_require_authentication()
    {
        await using var server = await TestApiServer.StartWithConfiguredAuthenticationAsync();

        using var createBudget = await server.Client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest("UK", "GBP"));
        using var listBudgets = await server.Client.GetAsync("/api/budgets");
        using var createTransaction = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest("Salary", "Credit", "2026-07-01", "3000.00", "GBP"));
        using var listTransactions = await server.Client.GetAsync("/api/transactions");
        using var getSummary = await server.Client.GetAsync($"/api/budgets/{Guid.NewGuid()}/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, createBudget.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listBudgets.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createTransaction.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listTransactions.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, getSummary.StatusCode);
    }

    [Fact]
    public async Task Operational_and_documentation_endpoints_remain_anonymous()
    {
        await using var server = await TestApiServer.StartWithConfiguredAuthenticationAsync();

        using var health = await server.Client.GetAsync("/health");
        using var version = await server.Client.GetAsync("/api/version");
        using var swagger = await server.Client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Configured_authentication_scheme_accepts_stable_application_identity()
    {
        await using var server = await TestApiServer.StartWithConfiguredAuthenticationAsync();
        server.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("BudgetyTzar", "user-a");

        var budget = await CreateBudgetAsync(server.Client, "UK", "GBP");

        Assert.Equal("UK", budget.Name);
    }

    [Fact]
    public async Task Budget_and_budget_item_operations_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userA = server.CreateClientForUser("user-a");
        using var userB = server.CreateClientForUser("user-b");
        var budget = await CreateBudgetAsync(userA, "Shared", "GBP");
        var item = await CreateBudgetItemAsync(userA, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var createWithIgnoredQuery = await userA.PostAsJsonAsync(
            "/api/budgets?userId=user-b",
            new CreateBudgetRequest("Query Ignored", "GBP"));
        createWithIgnoredQuery.EnsureSuccessStatusCode();

        var userBBudgets = await userB.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");
        using var userBGetBudget = await userB.GetAsync($"/api/budgets/{budget.BudgetId}");
        using var userBRenameBudget = await userB.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/name",
            new RenameBudgetRequest("Renamed"));
        using var userBCreateItem = await userB.PostAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Restaurants", "Consumption", "100.00"));
        using var userBListItems = await userB.GetAsync($"/api/budgets/{budget.BudgetId}/budget-items");
        using var userBGetItem = await userB.GetAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}");
        using var userBRenameItem = await userB.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}/name",
            new RenameBudgetItemRequest("Food"));
        using var userBChangeItem = await userB.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest("450.00"));
        using var userBDeleteItem = await userB.DeleteAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}");
        var userBBudget = await CreateBudgetAsync(userB, "Shared", "GBP");

        Assert.NotNull(userBBudgets);
        Assert.Empty(userBBudgets);
        Assert.Equal(HttpStatusCode.NotFound, userBGetBudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBRenameBudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBCreateItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBListItems.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBGetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBRenameItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBChangeItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeleteItem.StatusCode);
        Assert.NotEqual(budget.BudgetId, userBBudget.BudgetId);

        var userABudgets = await userA.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");
        Assert.NotNull(userABudgets);
        Assert.Equal(2, userABudgets.Count);
    }

    [Fact]
    public async Task Existing_resource_responses_do_not_expose_owner_identity()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userA = server.CreateClientForUser("user-a");

        using var response = await userA.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest("UK", "GBP"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("owner", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transaction_operations_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userA = server.CreateClientForUser("user-a");
        using var userB = server.CreateClientForUser("user-b");
        var transaction = await CreateTransactionAsync(userA, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");

        var userBTransactions = await userB.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>("/api/transactions");
        using var userBGetTransaction = await userB.GetAsync($"/api/transactions/{transaction.TransactionId}");
        using var userBDeleteTransaction = await userB.DeleteAsync($"/api/transactions/{transaction.TransactionId}");

        Assert.NotNull(userBTransactions);
        Assert.Empty(userBTransactions);
        Assert.Equal(HttpStatusCode.NotFound, userBGetTransaction.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeleteTransaction.StatusCode);

        var userATransaction = await userA.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{transaction.TransactionId}");
        Assert.Equal(transaction.TransactionId, userATransaction?.TransactionId);
    }

    [Fact]
    public async Task Allocation_operations_require_transaction_and_budget_item_to_have_the_same_owner()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userA = server.CreateClientForUser("user-a");
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(userA, "A", "GBP");
        var userAItem = await CreateBudgetItemAsync(userA, userABudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userATransaction = await CreateTransactionAsync(userA, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        var userBBudget = await CreateBudgetAsync(userB, "B", "GBP");
        var userBItem = await CreateBudgetItemAsync(userB, userBBudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userBTransaction = await CreateTransactionAsync(userB, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");

        using var bTransactionToAItem = await userB.PutAsJsonAsync(
            $"/api/transactions/{userBTransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userAItem.BudgetItemId));
        using var aTransactionToBItem = await userA.PutAsJsonAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userBItem.BudgetItemId));

        Assert.Equal(HttpStatusCode.NotFound, bTransactionToAItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, aTransactionToBItem.StatusCode);

        var allocation = await AllocateTransactionAsync(userA, userATransaction.TransactionId, userAItem.BudgetItemId);
        using var userBGetAllocation = await userB.GetAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");
        using var userBDeleteAllocation = await userB.DeleteAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");

        Assert.Equal(userAItem.BudgetItemId, allocation.BudgetItemId);
        Assert.Equal(HttpStatusCode.NotFound, userBGetAllocation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeleteAllocation.StatusCode);

        var userAAllocation = await userA.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");
        Assert.Equal(userAItem.BudgetItemId, userAAllocation?.BudgetItemId);
    }

    [Fact]
    public async Task Budget_summary_reports_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userA = server.CreateClientForUser("user-a");
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(userA, "A", "GBP");
        var userAItem = await CreateBudgetItemAsync(userA, userABudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userATransaction = await CreateTransactionAsync(userA, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(userA, userATransaction.TransactionId, userAItem.BudgetItemId);
        var userBBudget = await CreateBudgetAsync(userB, "B", "GBP");
        await CreateBudgetItemAsync(userB, userBBudget.BudgetId, "Groceries", "Consumption", "400.00");

        using var userBGetUserASummary = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}/summary");
        var userBSummary = await userB.GetFromJsonAsync<BudgetSummaryResponse>(
            $"/api/budgets/{userBBudget.BudgetId}/summary");

        Assert.Equal(HttpStatusCode.NotFound, userBGetUserASummary.StatusCode);
        Assert.NotNull(userBSummary);
        Assert.Equal("0.00", Assert.Single(userBSummary.Consumption.Items).ActualAmount);
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

    private static async Task<TransactionAllocationResponse> AllocateTransactionAsync(
        HttpClient client,
        Guid transactionId,
        Guid budgetItemId)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));

        response.EnsureSuccessStatusCode();

        var allocation = await response.Content.ReadFromJsonAsync<TransactionAllocationResponse>();
        return allocation ?? throw new InvalidOperationException("Allocate transaction response was empty.");
    }

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record RenameBudgetRequest(string Name);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

    private sealed record RenameBudgetItemRequest(string Name);

    private sealed record ChangeBudgetItemPlannedAmountRequest(string PlannedAmount);

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);

    private sealed record BudgetResponse(
        Guid BudgetId,
        string Name,
        string Currency,
        IReadOnlyList<BudgetItemResponse> BudgetItems);

    private sealed record BudgetListItemResponse(Guid BudgetId, string Name, string Currency);

    private sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);

    private sealed record TransactionResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record TransactionListItemResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record TransactionAllocationResponse(
        Guid TransactionId,
        Guid BudgetItemId,
        string Amount,
        string Currency);

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

using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests.Identity;

public sealed class AuthenticatedOwnershipApiTests
{
    [Theory]
    [InlineData("GET", "/api/budgets")]
    [InlineData("POST", "/api/budgets")]
    [InlineData("GET", "/api/transactions")]
    [InlineData("POST", "/api/transactions")]
    [InlineData("PUT", "/api/transactions/11111111-1111-1111-1111-111111111111/allocation")]
    [InlineData("GET", "/api/budgets/11111111-1111-1111-1111-111111111111/summary")]
    public async Task Business_api_requests_require_authentication(string method, string path)
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateUnauthenticatedClient();
        using var request = CreateRequest(method, path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_operational_endpoints_do_not_require_authentication()
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateUnauthenticatedClient();

        using var health = await client.GetAsync("/health");
        using var version = await client.GetAsync("/api/version");
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Budgeting_and_reporting_resources_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(server.Client, "Household", "GBP");
        var userABudgetItem = await CreateBudgetItemAsync(
            server.Client,
            userABudget.BudgetId,
            "Groceries",
            "Consumption",
            "400.00");

        var userBBudgetsBeforeCreate = await userB.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");
        var userBBudget = await CreateBudgetAsync(userB, "Household", "GBP");
        var userBBudgetsAfterCreate = await userB.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");

        using var getBudget = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}");
        using var renameBudget = await userB.PutAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/name",
            new RenameBudgetRequest("Renamed"));
        using var createBudgetItem = await userB.PostAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Restaurants", "Consumption", "200.00"));
        using var listBudgetItems = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}/budget-items");
        using var getBudgetItem = await userB.GetAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userABudgetItem.BudgetItemId}");
        using var renameBudgetItem = await userB.PutAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userABudgetItem.BudgetItemId}/name",
            new RenameBudgetItemRequest("Food"));
        using var changeBudgetItemPlannedAmount = await userB.PutAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userABudgetItem.BudgetItemId}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest("450.00"));
        using var deleteBudgetItem = await userB.DeleteAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userABudgetItem.BudgetItemId}");
        using var getSummary = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}/summary");

        Assert.NotNull(userBBudgetsBeforeCreate);
        Assert.Empty(userBBudgetsBeforeCreate);
        Assert.NotNull(userBBudgetsAfterCreate);
        Assert.Equal(userBBudget.BudgetId, Assert.Single(userBBudgetsAfterCreate).BudgetId);
        Assert.Equal(HttpStatusCode.NotFound, getBudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, renameBudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, createBudgetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, listBudgetItems.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getBudgetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, renameBudgetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, changeBudgetItemPlannedAmount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteBudgetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getSummary.StatusCode);

        var userABudgetAfterCrossUserRequests = await server.Client.GetFromJsonAsync<BudgetResponse>(
            $"/api/budgets/{userABudget.BudgetId}");

        Assert.Equal("Household", userABudgetAfterCrossUserRequests?.Name);
        Assert.Equal("Groceries", Assert.Single(userABudgetAfterCrossUserRequests?.BudgetItems ?? []).Name);
    }

    [Fact]
    public async Task Transactions_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userATransaction = await CreateTransactionAsync(
            server.Client,
            "Supermarket",
            "Debit",
            "2026-07-02",
            "42.50",
            "GBP");

        var userBTransactions = await userB.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>("/api/transactions");
        using var getTransaction = await userB.GetAsync($"/api/transactions/{userATransaction.TransactionId}");
        using var deleteTransaction = await userB.DeleteAsync($"/api/transactions/{userATransaction.TransactionId}");

        Assert.NotNull(userBTransactions);
        Assert.Empty(userBTransactions);
        Assert.Equal(HttpStatusCode.NotFound, getTransaction.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteTransaction.StatusCode);

        var userATransactionAfterCrossUserRequests = await server.Client.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{userATransaction.TransactionId}");

        Assert.Equal("Supermarket", userATransactionAfterCrossUserRequests?.Description);
    }

    [Fact]
    public async Task Allocations_require_the_transaction_and_budget_item_to_share_the_authenticated_owner()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(server.Client, "Household", "GBP");
        var userABudgetItem = await CreateBudgetItemAsync(
            server.Client,
            userABudget.BudgetId,
            "Groceries",
            "Consumption",
            "400.00");
        var userATransaction = await CreateTransactionAsync(
            server.Client,
            "Supermarket",
            "Debit",
            "2026-07-02",
            "42.50",
            "GBP");
        var userBBudget = await CreateBudgetAsync(userB, "Household", "GBP");
        var userBBudgetItem = await CreateBudgetItemAsync(
            userB,
            userBBudget.BudgetId,
            "Groceries",
            "Consumption",
            "400.00");

        using var userAToUserBBudgetItem = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userBBudgetItem.BudgetItemId));
        using var userBToUserATransaction = await userB.PutAsJsonAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userBBudgetItem.BudgetItemId));

        var allocation = await AllocateTransactionAsync(
            server.Client,
            userATransaction.TransactionId,
            userABudgetItem.BudgetItemId);

        using var userBGetAllocation = await userB.GetAsync($"/api/transactions/{userATransaction.TransactionId}/allocation");
        using var userBDeleteAllocation = await userB.DeleteAsync($"/api/transactions/{userATransaction.TransactionId}/allocation");
        var userAAllocationAfterCrossUserRequests = await server.Client.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");

        Assert.Equal(HttpStatusCode.NotFound, userAToUserBBudgetItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBToUserATransaction.StatusCode);
        Assert.Equal(userABudgetItem.BudgetItemId, allocation.BudgetItemId);
        Assert.Equal(HttpStatusCode.NotFound, userBGetAllocation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeleteAllocation.StatusCode);
        Assert.Equal(userABudgetItem.BudgetItemId, userAAllocationAfterCrossUserRequests?.BudgetItemId);
    }

    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method is "POST")
        {
            request.Content = JsonContent.Create(new CreateBudgetRequest("Household", "GBP"));
        }

        if (method is "PUT")
        {
            request.Content = JsonContent.Create(new AllocateTransactionRequest(Guid.NewGuid()));
        }

        return request;
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

    private sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems);

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
}

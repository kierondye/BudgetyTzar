using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionAllocationApiTests
{
    [Fact]
    public async Task Allocate_transaction_creates_full_amount_allocation()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(groceries.BudgetItemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var allocation = await response.Content.ReadFromJsonAsync<TransactionAllocationResponse>();

        Assert.NotNull(allocation);
        Assert.Equal(transaction.TransactionId, allocation.TransactionId);
        Assert.Equal(groceries.BudgetItemId, allocation.BudgetItemId);
        Assert.Equal("42.50", allocation.Amount);
        Assert.Equal("GBP", allocation.Currency);
    }

    [Fact]
    public async Task Allocate_transaction_to_same_budget_item_is_idempotent()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        var firstAllocation = await AllocateTransactionAsync(server, transaction.TransactionId, groceries.BudgetItemId);

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(groceries.BudgetItemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var secondAllocation = await response.Content.ReadFromJsonAsync<TransactionAllocationResponse>();

        Assert.Equal(firstAllocation, secondAllocation);
    }

    [Fact]
    public async Task Allocate_transaction_to_different_budget_item_is_rejected()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var restaurants = await CreateBudgetItemAsync(server, budget.BudgetId, "Restaurants", "Consumption", "200.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, groceries.BudgetItemId);

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(restaurants.BudgetItemId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var allocation = await server.Client.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{transaction.TransactionId}/allocation");

        Assert.NotNull(allocation);
        Assert.Equal(groceries.BudgetItemId, allocation.BudgetItemId);
    }

    [Fact]
    public async Task Allocate_transaction_returns_not_found_when_transaction_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}/allocation",
            new AllocateTransactionRequest(groceries.BudgetItemId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Allocate_transaction_returns_not_found_when_budget_item_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Allocate_transaction_rejects_currency_mismatch()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "EU", "EUR");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(groceries.BudgetItemId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Allocation_operations_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(server.Client, "User A", "GBP");
        var userAItem = await CreateBudgetItemAsync(server.Client, userABudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userATransaction = await CreateTransactionAsync(
            server.Client,
            "User A supermarket",
            "Debit",
            "2026-07-02",
            "42.50",
            "GBP");
        var userBBudget = await CreateBudgetAsync(userB, "User B", "GBP");
        var userBItem = await CreateBudgetItemAsync(userB, userBBudget.BudgetId, "Groceries", "Consumption", "100.00");
        var userBTransaction = await CreateTransactionAsync(
            userB,
            "User B supermarket",
            "Debit",
            "2026-07-02",
            "12.00",
            "GBP");

        await AllocateTransactionAsync(server.Client, userATransaction.TransactionId, userAItem.BudgetItemId);

        using var userBAllocatesToUserAItem = await userB.PutAsJsonAsync(
            $"/api/transactions/{userBTransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userAItem.BudgetItemId));
        using var userBAllocatesUserATransaction = await userB.PutAsJsonAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userBItem.BudgetItemId));
        using var userBGetsUserAAllocation = await userB.GetAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");
        using var userBDeletesUserAAllocation = await userB.DeleteAsync(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");

        Assert.Equal(HttpStatusCode.NotFound, userBAllocatesToUserAItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBAllocatesUserATransaction.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBGetsUserAAllocation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeletesUserAAllocation.StatusCode);

        var userAAllocation = await server.Client.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{userATransaction.TransactionId}/allocation");
        Assert.NotNull(userAAllocation);
        Assert.Equal(userAItem.BudgetItemId, userAAllocation.BudgetItemId);
    }

    [Fact]
    public async Task Get_transaction_allocation_returns_existing_allocation()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, groceries.BudgetItemId);

        var allocation = await server.Client.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{transaction.TransactionId}/allocation");

        Assert.NotNull(allocation);
        Assert.Equal(transaction.TransactionId, allocation.TransactionId);
        Assert.Equal(groceries.BudgetItemId, allocation.BudgetItemId);
        Assert.Equal("42.50", allocation.Amount);
        Assert.Equal("GBP", allocation.Currency);
    }

    [Fact]
    public async Task Get_transaction_allocation_returns_not_found_when_transaction_is_unallocated()
    {
        await using var server = await TestApiServer.StartAsync();
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        using var response = await server.Client.GetAsync($"/api/transactions/{transaction.TransactionId}/allocation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_transaction_allocation_removes_existing_allocation()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, groceries.BudgetItemId);

        using var deleteResponse = await server.Client.DeleteAsync($"/api/transactions/{transaction.TransactionId}/allocation");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getResponse = await server.Client.GetAsync($"/api/transactions/{transaction.TransactionId}/allocation");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_transaction_allocation_is_idempotent_when_transaction_is_unallocated()
    {
        await using var server = await TestApiServer.StartAsync();
        var transaction = await CreateTransactionAsync(server, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        using var response = await server.Client.DeleteAsync($"/api/transactions/{transaction.TransactionId}/allocation");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<TransactionAllocationResponse> AllocateTransactionAsync(
        TestApiServer server,
        Guid transactionId,
        Guid budgetItemId)
    {
        return await AllocateTransactionAsync(server.Client, transactionId, budgetItemId);
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

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

    private sealed record TransactionAllocationResponse(
        Guid TransactionId,
        Guid BudgetItemId,
        string Amount,
        string Currency);

    private sealed record TransactionResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record BudgetResponse(
        Guid BudgetId,
        string Name,
        string Currency,
        IReadOnlyList<BudgetItemResponse> BudgetItems);

    private sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);
}

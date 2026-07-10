using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests;

// These are host-level ownership tests using TestApiServer's deterministic
// authentication scheme. They verify API ownership behaviour after authentication,
// not production JWT bearer-token validation.
public sealed class OwnershipApiTests
{
    public static TheoryData<HttpMethod, string> ProtectedRoutes =>
        new()
        {
            { HttpMethod.Post, "/api/budgets" },
            { HttpMethod.Get, "/api/budgets" },
            { HttpMethod.Get, $"/api/budgets/{Guid.NewGuid()}" },
            { HttpMethod.Put, $"/api/budgets/{Guid.NewGuid()}/name" },
            { HttpMethod.Post, $"/api/budgets/{Guid.NewGuid()}/budget-items" },
            { HttpMethod.Get, $"/api/budgets/{Guid.NewGuid()}/budget-items" },
            { HttpMethod.Get, $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}" },
            { HttpMethod.Put, $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}/name" },
            { HttpMethod.Put, $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}/planned-amount" },
            { HttpMethod.Delete, $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}" },
            { HttpMethod.Get, $"/api/budgets/{Guid.NewGuid()}/summary" },
            { HttpMethod.Post, "/api/transactions" },
            { HttpMethod.Get, "/api/transactions" },
            { HttpMethod.Get, $"/api/transactions/{Guid.NewGuid()}" },
            { HttpMethod.Delete, $"/api/transactions/{Guid.NewGuid()}" },
            { HttpMethod.Put, $"/api/transactions/{Guid.NewGuid()}/allocation" },
            { HttpMethod.Get, $"/api/transactions/{Guid.NewGuid()}/allocation" },
            { HttpMethod.Delete, $"/api/transactions/{Guid.NewGuid()}/allocation" }
        };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Business_api_routes_reject_unauthenticated_requests(
        HttpMethod method,
        string path)
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateClient(null);
        using var request = new HttpRequestMessage(method, path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(" ", "test")]
    [InlineData("alice", " ")]
    [InlineData("alice", null)]
    public async Task Business_api_routes_reject_authenticated_requests_with_blank_identity_claims(
        string userId,
        string? provider)
    {
        await using var server = await TestApiServer.StartAsync();
        using var client = server.CreateClient(userId, provider);

        using var response = await client.GetAsync("/api/budgets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Same_external_subject_from_different_providers_gets_separate_owned_resources()
    {
        await using var server = await TestApiServer.StartAsync();
        using var providerA = server.CreateClient("shared-subject", "provider-a");
        using var providerB = server.CreateClient("shared-subject", "provider-b");

        var budget = await CreateBudgetAsync(providerA, "Provider A household", "GBP");

        Assert.Empty(await GetAsync<IReadOnlyList<BudgetResponse>>(providerB, "/api/budgets"));
        await AssertNotFoundAsync(providerB.GetAsync($"/api/budgets/{budget.BudgetId}"));

        var sameProviderBudget = await GetAsync<BudgetResponse>(
            providerA,
            $"/api/budgets/{budget.BudgetId}");
        Assert.Equal("Provider A household", sameProviderBudget.Name);
    }

    [Fact]
    public async Task Authenticated_users_cannot_observe_or_change_each_others_resources()
    {
        await using var server = await TestApiServer.StartAsync();
        using var alice = server.CreateClient("alice");
        using var bob = server.CreateClient("bob");

        var budget = await CreateBudgetAsync(alice, "Household", "GBP");
        var item = await CreateBudgetItemAsync(
            alice,
            budget.BudgetId,
            "Groceries",
            "Consumption",
            "400.00");
        var transaction = await CreateTransactionAsync(
            alice,
            "Private supermarket purchase",
            "Debit",
            "2026-07-02",
            "42.50",
            "GBP");
        await AllocateTransactionAsync(alice, transaction.TransactionId, item.BudgetItemId);

        Assert.Empty(await GetAsync<IReadOnlyList<BudgetResponse>>(bob, "/api/budgets"));
        Assert.Empty(await GetAsync<IReadOnlyList<TransactionResponse>>(bob, "/api/transactions"));

        await AssertNotFoundAsync(bob.GetAsync($"/api/budgets/{budget.BudgetId}"));
        await AssertNotFoundAsync(bob.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/name",
            new RenameRequest("Disclosed")));
        await AssertNotFoundAsync(bob.PostAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Leaked", "Consumption", "1.00")));
        await AssertNotFoundAsync(bob.GetAsync($"/api/budgets/{budget.BudgetId}/budget-items"));
        await AssertNotFoundAsync(bob.GetAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}"));
        await AssertNotFoundAsync(bob.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}/name",
            new RenameRequest("Disclosed")));
        await AssertNotFoundAsync(bob.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}/planned-amount",
            new ChangePlannedAmountRequest("1.00")));
        await AssertNotFoundAsync(bob.DeleteAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{item.BudgetItemId}"));
        await AssertNotFoundAsync(bob.GetAsync($"/api/budgets/{budget.BudgetId}/summary"));

        await AssertNotFoundAsync(bob.GetAsync($"/api/transactions/{transaction.TransactionId}"));
        await AssertNotFoundAsync(bob.DeleteAsync($"/api/transactions/{transaction.TransactionId}"));
        await AssertNotFoundAsync(bob.PutAsJsonAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation",
            new AllocateTransactionRequest(item.BudgetItemId)));
        await AssertNotFoundAsync(bob.GetAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation"));
        await AssertNotFoundAsync(bob.DeleteAsync(
            $"/api/transactions/{transaction.TransactionId}/allocation"));

        var bobsTransaction = await CreateTransactionAsync(
            bob,
            "Bob transaction",
            "Debit",
            "2026-07-03",
            "10.00",
            "GBP");
        await AssertNotFoundAsync(bob.PutAsJsonAsync(
            $"/api/transactions/{bobsTransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(item.BudgetItemId)));

        var bobsBudgetWithSameName = await CreateBudgetAsync(bob, "Household", "GBP");
        Assert.NotEqual(budget.BudgetId, bobsBudgetWithSameName.BudgetId);

        var aliceBudget = await GetAsync<BudgetResponse>(alice, $"/api/budgets/{budget.BudgetId}");
        var aliceTransaction = await GetAsync<TransactionResponse>(
            alice,
            $"/api/transactions/{transaction.TransactionId}");
        var aliceAllocation = await GetAsync<AllocationResponse>(
            alice,
            $"/api/transactions/{transaction.TransactionId}/allocation");
        var aliceSummary = await GetAsync<BudgetSummaryResponse>(
            alice,
            $"/api/budgets/{budget.BudgetId}/summary");

        Assert.Equal("Household", aliceBudget.Name);
        Assert.Equal("Groceries", Assert.Single(aliceBudget.BudgetItems).Name);
        Assert.Equal("Private supermarket purchase", aliceTransaction.Description);
        Assert.Equal(item.BudgetItemId, aliceAllocation.BudgetItemId);
        Assert.Equal("42.50", Assert.Single(aliceSummary.Consumption.Items).ActualAmount);
    }

    private static async Task AssertNotFoundAsync(Task<HttpResponseMessage> responseTask)
    {
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetFromJsonAsync<T>(path);
        return response ?? throw new InvalidOperationException($"The response from '{path}' was empty.");
    }

    private static async Task<BudgetResponse> CreateBudgetAsync(
        HttpClient client,
        string name,
        string currency)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest(name, currency));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BudgetResponse>()
            ?? throw new InvalidOperationException("Create budget response was empty.");
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
        return await response.Content.ReadFromJsonAsync<BudgetItemResponse>()
            ?? throw new InvalidOperationException("Create budget item response was empty.");
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
        return await response.Content.ReadFromJsonAsync<TransactionResponse>()
            ?? throw new InvalidOperationException("Create transaction response was empty.");
    }

    private static async Task AllocateTransactionAsync(
        HttpClient client,
        Guid transactionId,
        Guid budgetItemId)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));
        response.EnsureSuccessStatusCode();
    }

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record RenameRequest(string Name);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

    private sealed record ChangePlannedAmountRequest(string PlannedAmount);

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

    private sealed record BudgetItemResponse(
        Guid BudgetItemId,
        string Name,
        string Kind,
        string PlannedAmount);

    private sealed record TransactionResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record AllocationResponse(
        Guid TransactionId,
        Guid BudgetItemId,
        string Amount,
        string Currency);

    private sealed record BudgetSummaryResponse(BudgetSummarySectionResponse Consumption);

    private sealed record BudgetSummarySectionResponse(IReadOnlyList<BudgetSummaryItemResponse> Items);

    private sealed record BudgetSummaryItemResponse(string ActualAmount);
}

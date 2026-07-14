using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Persistence.PostgreSql;
using BudgetyTzar.Tests.Support;
using BudgetyTzar.Tests.Support.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlPersistenceApiTests(PostgreSqlApiTestDatabase database)
    : IClassFixture<PostgreSqlApiTestDatabase>
{
    [Fact]
    public async Task PostgreSql_provider_preserves_public_api_behaviour_and_persists_data_across_server_instances()
    {
        var budgetName = $"Durable {Guid.NewGuid():N}";
        Guid budgetId;
        Guid groceriesId;
        Guid restaurantId;
        Guid groceryTransactionId;

        await using (var server = await TestApiServer.StartWithPostgreSqlAsync(database.ConnectionString))
        {
            var budget = await CreateBudgetAsync(server.Client, budgetName, "GBP");
            budgetId = budget.BudgetId;
            var salary = await CreateBudgetItemAsync(server.Client, budget.BudgetId, "Salary", "Funding", "3000.00");
            groceriesId = (await CreateBudgetItemAsync(server.Client, budget.BudgetId, "Groceries", "Consumption", "400.00")).BudgetItemId;
            restaurantId = (await CreateBudgetItemAsync(server.Client, budget.BudgetId, "Restaurants", "Consumption", "100.00")).BudgetItemId;
            var euroBudget = await CreateBudgetAsync(server.Client, $"{budgetName} EUR", "EUR");
            var euroItem = await CreateBudgetItemAsync(server.Client, euroBudget.BudgetId, "Travel", "Consumption", "75.00");

            var salaryTransaction = await CreateTransactionAsync(server.Client, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
            var groceryTransaction = await CreateTransactionAsync(server.Client, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");
            groceryTransactionId = groceryTransaction.TransactionId;
            var refundTransaction = await CreateTransactionAsync(server.Client, "Grocery refund", "Credit", "2026-07-03", "10.00", "GBP");

            await AllocateTransactionAsync(server.Client, salaryTransaction.TransactionId, salary.BudgetItemId);
            await AllocateTransactionAsync(server.Client, groceryTransaction.TransactionId, groceriesId);
            await AllocateTransactionAsync(server.Client, refundTransaction.TransactionId, groceriesId);

            var idempotentAllocation = await AllocateTransactionAsync(server.Client, groceryTransaction.TransactionId, groceriesId);
            using var differentItemResponse = await server.Client.PutAsJsonAsync(
                $"/api/transactions/{groceryTransaction.TransactionId}/allocation",
                new AllocateTransactionRequest(restaurantId));
            using var currencyMismatchResponse = await server.Client.PutAsJsonAsync(
                $"/api/transactions/{groceryTransaction.TransactionId}/allocation",
                new AllocateTransactionRequest(euroItem.BudgetItemId));

            var summary = await server.Client.GetFromJsonAsync<BudgetSummaryResponse>($"/api/budgets/{budget.BudgetId}/summary");

            Assert.Equal(groceryTransaction.TransactionId, idempotentAllocation.TransactionId);
            Assert.Equal(groceriesId, idempotentAllocation.BudgetItemId);
            Assert.Equal(HttpStatusCode.Conflict, differentItemResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, currencyMismatchResponse.StatusCode);
            Assert.NotNull(summary);
            Assert.Equal(budget.BudgetId, summary.BudgetId);
            Assert.Equal(budgetName, summary.Name);
            AssertSectionTotals("3000.00", "3000.00", "0.00", summary.Funding);
            AssertSectionTotals("500.00", "32.50", "467.50", summary.Consumption);
            Assert.Equal("2500.00", summary.Overall.PlannedSurplus);
            Assert.Equal("2967.50", summary.Overall.ActualSurplus);
        }

        await using (var restartedServer = await TestApiServer.StartWithPostgreSqlAsync(database.ConnectionString))
        {
            var persistedBudget = await restartedServer.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budgetId}");
            var persistedAllocation = await restartedServer.Client.GetFromJsonAsync<TransactionAllocationResponse>(
                $"/api/transactions/{groceryTransactionId}/allocation");
            var persistedSummary = await restartedServer.Client.GetFromJsonAsync<BudgetSummaryResponse>($"/api/budgets/{budgetId}/summary");
            using var deleteAllocationResponse = await restartedServer.Client.DeleteAsync(
                $"/api/transactions/{groceryTransactionId}/allocation");
            using var removedAllocationResponse = await restartedServer.Client.GetAsync(
                $"/api/transactions/{groceryTransactionId}/allocation");
            var summaryAfterAllocationRemoval = await restartedServer.Client.GetFromJsonAsync<BudgetSummaryResponse>(
                $"/api/budgets/{budgetId}/summary");

            Assert.NotNull(persistedBudget);
            Assert.Equal(budgetName, persistedBudget.Name);
            Assert.Contains(persistedBudget.BudgetItems, item => item.BudgetItemId == groceriesId);
            Assert.NotNull(persistedAllocation);
            Assert.Equal(groceriesId, persistedAllocation.BudgetItemId);
            Assert.NotNull(persistedSummary);
            AssertSectionTotals("500.00", "32.50", "467.50", persistedSummary.Consumption);
            Assert.Equal(HttpStatusCode.NoContent, deleteAllocationResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, removedAllocationResponse.StatusCode);
            Assert.NotNull(summaryAfterAllocationRemoval);
            AssertSectionTotals("500.00", "-10.00", "510.00", summaryAfterAllocationRemoval.Consumption);
            Assert.Equal("3010.00", summaryAfterAllocationRemoval.Overall.ActualSurplus);
        }
    }

    [Fact]
    public async Task PostgreSql_provider_uses_durable_user_store_and_preserves_cross_user_non_disclosure()
    {
        var sharedName = $"Shared {Guid.NewGuid():N}";
        await using var server = await TestApiServer.StartWithPostgreSqlAsync(database.ConnectionString);
        using var userB = server.CreateClientForUser($"postgres-user-b-{Guid.NewGuid():N}");

        var userABudget = await CreateBudgetAsync(server.Client, sharedName, "GBP");
        var userAItem = await CreateBudgetItemAsync(server.Client, userABudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userATransaction = await CreateTransactionAsync(server.Client, "Supermarket", "Debit", "2026-07-02", "42.50", "GBP");
        var userBBudget = await CreateBudgetAsync(userB, sharedName, "GBP");
        var userBTransaction = await CreateTransactionAsync(userB, "Own supermarket", "Debit", "2026-07-02", "42.50", "GBP");

        var userBBudgets = await userB.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");
        using var userBGetUserABudget = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}");
        using var userBGetUserATransaction = await userB.GetAsync($"/api/transactions/{userATransaction.TransactionId}");
        using var userBAllocateToUserAItem = await userB.PutAsJsonAsync(
            $"/api/transactions/{userBTransaction.TransactionId}/allocation",
            new AllocateTransactionRequest(userAItem.BudgetItemId));

        Assert.NotNull(userBBudgets);
        var listedBudget = Assert.Single(userBBudgets);
        Assert.Equal(userBBudget.BudgetId, listedBudget.BudgetId);
        Assert.Equal(HttpStatusCode.NotFound, userBGetUserABudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBGetUserATransaction.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBAllocateToUserAItem.StatusCode);
    }

    [Fact]
    public void PostgreSql_provider_requires_connection_configuration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApiApplication.Create(
                ["--urls", "http://127.0.0.1:0"],
                builder =>
                {
                    builder.Configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("Persistence:Provider", "PostgreSql")
                    ]);
                }));

        Assert.Contains("Persistence:Provider=PostgreSql requires", exception.Message, StringComparison.Ordinal);
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

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

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

public sealed class PostgreSqlApiTestDatabase : IAsyncLifetime
{
    private readonly PostgreSqlTestDatabase database = new();

    public string ConnectionString => database.ConnectionString;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        return database.DisposeAsync();
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}

using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionApiTests
{
    [Fact]
    public async Task Create_transaction_returns_recorded_transaction()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest("Salary", "Credit", "2026-07-01", "3000.00", "GBP"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var transaction = await response.Content.ReadFromJsonAsync<TransactionResponse>();

        Assert.NotNull(transaction);
        Assert.NotEqual(Guid.Empty, transaction.TransactionId);
        Assert.Equal("Salary", transaction.Description);
        Assert.Equal("Credit", transaction.Type);
        Assert.Equal("2026-07-01", transaction.TransactionDate);
        Assert.Equal("3000.00", transaction.Amount);
        Assert.Equal("GBP", transaction.Currency);
        Assert.Equal($"/api/transactions/{transaction.TransactionId}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task List_transactions_returns_recorded_transactions()
    {
        await using var server = await TestApiServer.StartAsync();

        var salary = await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>("/api/transactions");

        Assert.NotNull(transactions);
        Assert.Collection(
            transactions,
            transaction =>
            {
                Assert.Equal(salary.TransactionId, transaction.TransactionId);
                Assert.Equal("Salary", transaction.Description);
                Assert.Equal("Credit", transaction.Type);
                Assert.Equal("2026-07-01", transaction.TransactionDate);
                Assert.Equal("3000.00", transaction.Amount);
                Assert.Equal("GBP", transaction.Currency);
            },
            transaction =>
            {
                Assert.Equal(groceries.TransactionId, transaction.TransactionId);
                Assert.Equal("Groceries", transaction.Description);
                Assert.Equal("Debit", transaction.Type);
                Assert.Equal("2026-07-02", transaction.TransactionDate);
                Assert.Equal("42.50", transaction.Amount);
                Assert.Equal("GBP", transaction.Currency);
            });
    }

    [Fact]
    public async Task Get_transaction_returns_transaction_details()
    {
        await using var server = await TestApiServer.StartAsync();
        var createdTransaction = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");

        var transaction = await server.Client.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{createdTransaction.TransactionId}");

        Assert.NotNull(transaction);
        Assert.Equal(createdTransaction.TransactionId, transaction.TransactionId);
        Assert.Equal("Groceries", transaction.Description);
        Assert.Equal("Debit", transaction.Type);
        Assert.Equal("2026-07-02", transaction.TransactionDate);
        Assert.Equal("42.50", transaction.Amount);
        Assert.Equal("GBP", transaction.Currency);
    }

    [Fact]
    public async Task Get_transaction_returns_not_found_when_transaction_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync($"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_transaction_removes_recorded_transaction()
    {
        await using var server = await TestApiServer.StartAsync();
        var createdTransaction = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");

        using var deleteResponse = await server.Client.DeleteAsync($"/api/transactions/{createdTransaction.TransactionId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getResponse = await server.Client.GetAsync($"/api/transactions/{createdTransaction.TransactionId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>("/api/transactions");
        Assert.NotNull(transactions);
        Assert.Empty(transactions);
    }

    [Fact]
    public async Task Delete_transaction_returns_conflict_when_transaction_is_allocated()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var budgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, budgetItem.BudgetItemId);

        using var response = await server.Client.DeleteAsync($"/api/transactions/{transaction.TransactionId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var retrievedTransaction = await server.Client.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{transaction.TransactionId}");
        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>("/api/transactions");

        Assert.NotNull(retrievedTransaction);
        Assert.Equal(transaction.TransactionId, retrievedTransaction.TransactionId);
        Assert.NotNull(transactions);
        Assert.Equal(transaction.TransactionId, Assert.Single(transactions).TransactionId);
    }

    [Fact]
    public async Task Delete_transaction_returns_not_found_when_transaction_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.DeleteAsync($"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Credit", "2026-07-01", "10.00", "GBP", "description")]
    [InlineData("Coffee", "", "2026-07-01", "10.00", "GBP", "type")]
    [InlineData("Coffee", "credit", "2026-07-01", "10.00", "GBP", "type")]
    [InlineData("Coffee", "Debit", "", "10.00", "GBP", "transactionDate")]
    [InlineData("Coffee", "Debit", "07/01/2026", "10.00", "GBP", "transactionDate")]
    [InlineData("Coffee", "Debit", "2026-07-01", "", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "0.00", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "-1.00", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "1", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "1.0", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "1.000", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "100000000.00", "GBP", "amount")]
    [InlineData("Coffee", "Debit", "2026-07-01", "10.00", "", "currency")]
    [InlineData("Coffee", "Debit", "2026-07-01", "10.00", "gbp", "currency")]
    public async Task Create_transaction_rejects_invalid_input(
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency,
        string expectedErrorKey)
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest(description, type, transactionDate, amount, currency));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains(expectedErrorKey, problem.Errors.Keys);
    }

    private static async Task<TransactionResponse> CreateTransactionAsync(
        TestApiServer server,
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        using var response = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest(description, type, transactionDate, amount, currency));

        response.EnsureSuccessStatusCode();

        var transaction = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        return transaction ?? throw new InvalidOperationException("Create transaction response was empty.");
    }

    private static async Task<BudgetResponse> CreateBudgetAsync(TestApiServer server, string name, string currency)
    {
        using var response = await server.Client.PostAsJsonAsync(
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
        using var response = await server.Client.PostAsJsonAsync(
            $"/api/budgets/{budgetId}/budget-items",
            new CreateBudgetItemRequest(name, kind, plannedAmount));

        response.EnsureSuccessStatusCode();

        var budgetItem = await response.Content.ReadFromJsonAsync<BudgetItemResponse>();
        return budgetItem ?? throw new InvalidOperationException("Create budget item response was empty.");
    }

    private static async Task AllocateTransactionAsync(TestApiServer server, Guid transactionId, Guid budgetItemId)
    {
        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));

        response.EnsureSuccessStatusCode();
    }

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);

    private sealed record TransactionResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems);

    private sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);

    private sealed record TransactionListItemResponse(
        Guid TransactionId,
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record ValidationProblemResponse(IDictionary<string, string[]> Errors);
}

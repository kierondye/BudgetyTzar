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

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

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

    private sealed record ValidationProblemResponse(IDictionary<string, string[]> Errors);
}

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
    public async Task List_transactions_filters_by_from_transaction_date()
    {
        await using var server = await TestApiServer.StartAsync();

        await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        var coffee = await CreateTransactionAsync(server, "Coffee", "Debit", "2026-07-03", "3.50", "GBP");

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?from=2026-07-02");

        Assert.NotNull(transactions);
        Assert.Collection(
            transactions,
            transaction => Assert.Equal(groceries.TransactionId, transaction.TransactionId),
            transaction => Assert.Equal(coffee.TransactionId, transaction.TransactionId));
    }

    [Fact]
    public async Task List_transactions_filters_by_to_transaction_date()
    {
        await using var server = await TestApiServer.StartAsync();

        var salary = await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await CreateTransactionAsync(server, "Coffee", "Debit", "2026-07-03", "3.50", "GBP");

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?to=2026-07-02");

        Assert.NotNull(transactions);
        Assert.Collection(
            transactions,
            transaction => Assert.Equal(salary.TransactionId, transaction.TransactionId),
            transaction => Assert.Equal(groceries.TransactionId, transaction.TransactionId));
    }

    [Fact]
    public async Task List_transactions_combines_date_filters()
    {
        await using var server = await TestApiServer.StartAsync();

        await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await CreateTransactionAsync(server, "Coffee", "Debit", "2026-07-03", "3.50", "GBP");

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?from=2026-07-02&to=2026-07-02");

        Assert.NotNull(transactions);
        var transaction = Assert.Single(transactions);
        Assert.Equal(groceries.TransactionId, transaction.TransactionId);
    }

    [Fact]
    public async Task List_transactions_filters_by_allocation_status()
    {
        await using var server = await TestApiServer.StartAsync();

        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceriesBudgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var salary = await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, groceries.TransactionId, groceriesBudgetItem.BudgetItemId);

        var defaultTransactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions");
        var allTransactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?allocationStatus=all");
        var unallocatedTransactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?allocationStatus=unallocated");
        var allocatedTransactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?allocationStatus=allocated");

        AssertTransactionIds(defaultTransactions, salary.TransactionId, groceries.TransactionId);
        AssertTransactionIds(allTransactions, salary.TransactionId, groceries.TransactionId);
        AssertTransactionIds(unallocatedTransactions, salary.TransactionId);
        AssertTransactionIds(allocatedTransactions, groceries.TransactionId);
    }

    [Fact]
    public async Task List_transactions_combines_date_and_allocation_status_filters()
    {
        await using var server = await TestApiServer.StartAsync();

        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceriesBudgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        await CreateTransactionAsync(server, "Salary", "Credit", "2026-07-01", "3000.00", "GBP");
        var groceries = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        var coffee = await CreateTransactionAsync(server, "Coffee", "Debit", "2026-07-03", "3.50", "GBP");
        await AllocateTransactionAsync(server, groceries.TransactionId, groceriesBudgetItem.BudgetItemId);

        var transactions = await server.Client.GetFromJsonAsync<IReadOnlyList<TransactionListItemResponse>>(
            "/api/transactions?from=2026-07-02&allocationStatus=unallocated");

        AssertTransactionIds(transactions, coffee.TransactionId);
    }

    [Theory]
    [InlineData("from=2026-02-31", "from")]
    [InlineData("from=07/01/2026", "from")]
    [InlineData("to=2026-02-31", "to")]
    [InlineData("to=2026-7-1", "to")]
    public async Task List_transactions_rejects_invalid_date_filters(string query, string expectedErrorKey)
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync($"/api/transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains(expectedErrorKey, problem.Errors.Keys);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("Allocated")]
    public async Task List_transactions_rejects_invalid_allocation_status_filters(string allocationStatus)
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync($"/api/transactions?allocationStatus={allocationStatus}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains("allocationStatus", problem.Errors.Keys);
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

    private static async Task AllocateTransactionAsync(
        TestApiServer server,
        Guid transactionId,
        Guid budgetItemId)
    {
        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));

        response.EnsureSuccessStatusCode();
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

    private static void AssertTransactionIds(
        IReadOnlyList<TransactionListItemResponse>? transactions,
        params Guid[] expectedTransactionIds)
    {
        Assert.NotNull(transactions);
        Assert.Equal(expectedTransactionIds, transactions.Select(transaction => transaction.TransactionId));
    }

    private sealed record CreateTransactionRequest(
        string Description,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record AllocateTransactionRequest(Guid BudgetItemId);

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record CreateBudgetItemRequest(string Name, string Kind, string PlannedAmount);

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

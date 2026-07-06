using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Tests.Support;

namespace BudgetyTzar.Tests.Budgeting;

public sealed class BudgetApiTests
{
    [Fact]
    public async Task Create_budget_returns_created_budget_without_budget_items()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest("UK", "GBP"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var budget = await response.Content.ReadFromJsonAsync<BudgetResponse>();

        Assert.NotNull(budget);
        Assert.NotEqual(Guid.Empty, budget.BudgetId);
        Assert.Equal("UK", budget.Name);
        Assert.Equal("GBP", budget.Currency);
        Assert.Empty(budget.BudgetItems);
        Assert.Equal($"/api/budgets/{budget.BudgetId}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task List_budgets_returns_created_budgets()
    {
        await using var server = await TestApiServer.StartAsync();

        var ukBudget = await CreateBudgetAsync(server, "UK", "GBP");
        var euBudget = await CreateBudgetAsync(server, "EU", "EUR");

        var budgets = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");

        Assert.NotNull(budgets);
        Assert.Collection(
            budgets,
            budget =>
            {
                Assert.Equal(ukBudget.BudgetId, budget.BudgetId);
                Assert.Equal("UK", budget.Name);
                Assert.Equal("GBP", budget.Currency);
            },
            budget =>
            {
                Assert.Equal(euBudget.BudgetId, budget.BudgetId);
                Assert.Equal("EU", budget.Name);
                Assert.Equal("EUR", budget.Currency);
            });
    }

    [Fact]
    public async Task Get_budget_returns_budget_details()
    {
        await using var server = await TestApiServer.StartAsync();
        var createdBudget = await CreateBudgetAsync(server, "Household", "GBP");

        var budget = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{createdBudget.BudgetId}");

        Assert.NotNull(budget);
        Assert.Equal(createdBudget.BudgetId, budget.BudgetId);
        Assert.Equal("Household", budget.Name);
        Assert.Equal("GBP", budget.Currency);
        Assert.Empty(budget.BudgetItems);
    }

    [Fact]
    public async Task Get_budget_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.GetAsync($"/api/budgets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "GBP", "name")]
    [InlineData("UK", "", "currency")]
    [InlineData("UK", "gbp", "currency")]
    [InlineData("UK", "GB", "currency")]
    public async Task Create_budget_rejects_invalid_input(string name, string currency, string expectedErrorKey)
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest(name, currency));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains(expectedErrorKey, problem.Errors.Keys);
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

    private sealed record CreateBudgetRequest(string Name, string Currency);

    private sealed record BudgetResponse(Guid BudgetId, string Name, string Currency, IReadOnlyList<BudgetItemResponse> BudgetItems);

    private sealed record BudgetListItemResponse(Guid BudgetId, string Name, string Currency);

    private sealed record BudgetItemResponse(Guid BudgetItemId, string Name, string Kind, string PlannedAmount);

    private sealed record ValidationProblemResponse(IDictionary<string, string[]> Errors);
}

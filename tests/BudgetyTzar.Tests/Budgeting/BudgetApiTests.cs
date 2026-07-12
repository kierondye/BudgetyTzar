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
    public async Task Budgeting_operations_are_scoped_to_the_authenticated_user()
    {
        await using var server = await TestApiServer.StartAsync();
        using var userB = server.CreateClientForUser("user-b");
        var userABudget = await CreateBudgetAsync(server.Client, "Shared", "GBP");
        var userAItem = await CreateBudgetItemAsync(server.Client, userABudget.BudgetId, "Groceries", "Consumption", "400.00");
        var userBBudget = await CreateBudgetAsync(userB, "Shared", "GBP");

        var userBBudgets = await userB.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");
        using var userBGetUserABudget = await userB.GetAsync($"/api/budgets/{userABudget.BudgetId}");
        using var userBRenameUserABudget = await userB.PutAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/name",
            new RenameBudgetRequest("Renamed by B"));
        using var userBCreateItemForUserABudget = await userB.PostAsJsonAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Restaurants", "Consumption", "100.00"));
        using var userBGetUserAItem = await userB.GetAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userAItem.BudgetItemId}");
        using var userBDeleteUserAItem = await userB.DeleteAsync(
            $"/api/budgets/{userABudget.BudgetId}/budget-items/{userAItem.BudgetItemId}");

        Assert.NotNull(userBBudgets);
        var userBListedBudget = Assert.Single(userBBudgets);
        Assert.Equal(userBBudget.BudgetId, userBListedBudget.BudgetId);
        Assert.Equal(HttpStatusCode.NotFound, userBGetUserABudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBRenameUserABudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBCreateItemForUserABudget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBGetUserAItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userBDeleteUserAItem.StatusCode);

        var userABudgetAfterUserBRequests = await server.Client.GetFromJsonAsync<BudgetResponse>(
            $"/api/budgets/{userABudget.BudgetId}");
        Assert.Equal("Shared", userABudgetAfterUserBRequests?.Name);
        Assert.Equal(userAItem.BudgetItemId, Assert.Single(userABudgetAfterUserBRequests?.BudgetItems ?? []).BudgetItemId);
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

    [Fact]
    public async Task Create_budget_returns_conflict_when_budget_name_already_exists()
    {
        await using var server = await TestApiServer.StartAsync();
        await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PostAsJsonAsync(
            "/api/budgets",
            new CreateBudgetRequest("UK", "GBP"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rename_budget_updates_budget_name()
    {
        await using var server = await TestApiServer.StartAsync();
        var createdBudget = await CreateBudgetAsync(server, "UK", "GBP");

        using var renameResponse = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{createdBudget.BudgetId}/name",
            new RenameBudgetRequest("UK 2026"));

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var renamedBudget = await renameResponse.Content.ReadFromJsonAsync<BudgetResponse>();
        var retrievedBudget = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{createdBudget.BudgetId}");
        var budgets = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetListItemResponse>>("/api/budgets");

        Assert.NotNull(renamedBudget);
        Assert.Equal(createdBudget.BudgetId, renamedBudget.BudgetId);
        Assert.Equal("UK 2026", renamedBudget.Name);
        Assert.Equal("UK 2026", retrievedBudget?.Name);
        Assert.Equal("UK 2026", Assert.Single(budgets ?? []).Name);
    }

    [Fact]
    public async Task Rename_budget_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{Guid.NewGuid()}/name",
            new RenameBudgetRequest("UK 2026"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rename_budget_rejects_empty_name()
    {
        await using var server = await TestApiServer.StartAsync();
        var createdBudget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{createdBudget.BudgetId}/name",
            new RenameBudgetRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains("name", problem.Errors.Keys);
    }

    [Fact]
    public async Task Rename_budget_returns_conflict_when_budget_name_already_exists()
    {
        await using var server = await TestApiServer.StartAsync();
        var ukBudget = await CreateBudgetAsync(server, "UK", "GBP");
        await CreateBudgetAsync(server, "EU", "EUR");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{ukBudget.BudgetId}/name",
            new RenameBudgetRequest("EU"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_budget_item_returns_created_budget_item()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PostAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Salary", "Funding", "3000.00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var budgetItem = await response.Content.ReadFromJsonAsync<BudgetItemResponse>();

        Assert.NotNull(budgetItem);
        Assert.NotEqual(Guid.Empty, budgetItem.BudgetItemId);
        Assert.Equal("Salary", budgetItem.Name);
        Assert.Equal("Funding", budgetItem.Kind);
        Assert.Equal("3000.00", budgetItem.PlannedAmount);
        Assert.Equal(
            $"/api/budgets/{budget.BudgetId}/budget-items/{budgetItem.BudgetItemId}",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_budget_includes_created_budget_items()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        var budgetDetails = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budget.BudgetId}");

        Assert.NotNull(budgetDetails);
        Assert.Collection(
            budgetDetails.BudgetItems,
            budgetItem => Assert.Equal(salary.BudgetItemId, budgetItem.BudgetItemId),
            budgetItem => Assert.Equal(groceries.BudgetItemId, budgetItem.BudgetItemId));
    }

    [Fact]
    public async Task List_budget_items_returns_created_budget_items()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        var budgetItems = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetItemResponse>>(
            $"/api/budgets/{budget.BudgetId}/budget-items");

        Assert.NotNull(budgetItems);
        Assert.Collection(
            budgetItems,
            budgetItem => Assert.Equal(salary.BudgetItemId, budgetItem.BudgetItemId),
            budgetItem => Assert.Equal(groceries.BudgetItemId, budgetItem.BudgetItemId));
    }

    [Fact]
    public async Task Get_budget_item_returns_budget_item()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var createdBudgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        var budgetItem = await server.Client.GetFromJsonAsync<BudgetItemResponse>(
            $"/api/budgets/{budget.BudgetId}/budget-items/{createdBudgetItem.BudgetItemId}");

        Assert.NotNull(budgetItem);
        Assert.Equal(createdBudgetItem.BudgetItemId, budgetItem.BudgetItemId);
        Assert.Equal("Groceries", budgetItem.Name);
        Assert.Equal("Consumption", budgetItem.Kind);
        Assert.Equal("400.00", budgetItem.PlannedAmount);
    }

    [Fact]
    public async Task Rename_budget_item_updates_budget_item_name()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");

        using var renameResponse = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{salary.BudgetItemId}/name",
            new RenameBudgetItemRequest("Pay"));

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var renamedBudgetItem = await renameResponse.Content.ReadFromJsonAsync<BudgetItemResponse>();
        var budgetDetails = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budget.BudgetId}");
        var budgetItems = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetItemResponse>>(
            $"/api/budgets/{budget.BudgetId}/budget-items");
        var retrievedBudgetItem = await server.Client.GetFromJsonAsync<BudgetItemResponse>(
            $"/api/budgets/{budget.BudgetId}/budget-items/{salary.BudgetItemId}");

        Assert.NotNull(renamedBudgetItem);
        Assert.Equal(salary.BudgetItemId, renamedBudgetItem.BudgetItemId);
        Assert.Equal("Pay", renamedBudgetItem.Name);
        Assert.Equal("Funding", renamedBudgetItem.Kind);
        Assert.Equal("3000.00", renamedBudgetItem.PlannedAmount);
        Assert.Equal("Pay", Assert.Single(budgetDetails?.BudgetItems ?? []).Name);
        Assert.Equal("Pay", Assert.Single(budgetItems ?? []).Name);
        Assert.Equal("Pay", retrievedBudgetItem?.Name);
    }

    [Fact]
    public async Task Rename_budget_item_rejects_empty_name()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{salary.BudgetItemId}/name",
            new RenameBudgetItemRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains("name", problem.Errors.Keys);
    }

    [Fact]
    public async Task Rename_budget_item_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}/name",
            new RenameBudgetItemRequest("Pay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rename_budget_item_returns_not_found_when_budget_item_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{Guid.NewGuid()}/name",
            new RenameBudgetItemRequest("Pay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rename_budget_item_returns_conflict_when_budget_item_name_already_exists()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var salary = await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{salary.BudgetItemId}/name",
            new RenameBudgetItemRequest("Groceries"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Change_budget_item_planned_amount_updates_budget_item_planned_amount()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var updateResponse = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{groceries.BudgetItemId}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest("450.00"));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedBudgetItem = await updateResponse.Content.ReadFromJsonAsync<BudgetItemResponse>();
        var budgetDetails = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budget.BudgetId}");
        var budgetItems = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetItemResponse>>(
            $"/api/budgets/{budget.BudgetId}/budget-items");
        var retrievedBudgetItem = await server.Client.GetFromJsonAsync<BudgetItemResponse>(
            $"/api/budgets/{budget.BudgetId}/budget-items/{groceries.BudgetItemId}");

        Assert.NotNull(updatedBudgetItem);
        Assert.Equal(groceries.BudgetItemId, updatedBudgetItem.BudgetItemId);
        Assert.Equal("Groceries", updatedBudgetItem.Name);
        Assert.Equal("Consumption", updatedBudgetItem.Kind);
        Assert.Equal("450.00", updatedBudgetItem.PlannedAmount);
        Assert.Equal("450.00", Assert.Single(budgetDetails?.BudgetItems ?? []).PlannedAmount);
        Assert.Equal("450.00", Assert.Single(budgetItems ?? []).PlannedAmount);
        Assert.Equal("450.00", retrievedBudgetItem?.PlannedAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.00")]
    [InlineData("-1.00")]
    [InlineData("1.001")]
    [InlineData("100000000.00")]
    public async Task Change_budget_item_planned_amount_rejects_invalid_money_values(string plannedAmount)
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var groceries = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{groceries.BudgetItemId}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest(plannedAmount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains("plannedAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task Change_budget_item_planned_amount_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest("450.00"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Change_budget_item_planned_amount_returns_not_found_when_budget_item_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PutAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{Guid.NewGuid()}/planned-amount",
            new ChangeBudgetItemPlannedAmountRequest("450.00"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_budget_item_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.PostAsJsonAsync(
            $"/api/budgets/{Guid.NewGuid()}/budget-items",
            new CreateBudgetItemRequest("Salary", "Funding", "3000.00"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_budget_item_returns_conflict_when_budget_item_name_already_exists()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        await CreateBudgetItemAsync(server, budget.BudgetId, "Salary", "Funding", "3000.00");

        using var response = await server.Client.PostAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items",
            new CreateBudgetItemRequest("Salary", "Consumption", "100.00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_budget_item_returns_not_found_when_budget_item_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.GetAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_budget_item_removes_recorded_budget_item()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var budgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");

        using var deleteResponse = await server.Client.DeleteAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{budgetItem.BudgetItemId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getResponse = await server.Client.GetAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{budgetItem.BudgetItemId}");
        var budgetItems = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetItemResponse>>(
            $"/api/budgets/{budget.BudgetId}/budget-items");
        var budgetDetails = await server.Client.GetFromJsonAsync<BudgetResponse>($"/api/budgets/{budget.BudgetId}");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.NotNull(budgetItems);
        Assert.Empty(budgetItems);
        Assert.NotNull(budgetDetails);
        Assert.Empty(budgetDetails.BudgetItems);
    }

    [Fact]
    public async Task Delete_budget_item_returns_conflict_when_budget_item_is_allocated()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");
        var budgetItem = await CreateBudgetItemAsync(server, budget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, budgetItem.BudgetItemId);

        using var response = await server.Client.DeleteAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{budgetItem.BudgetItemId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var retrievedBudgetItem = await server.Client.GetFromJsonAsync<BudgetItemResponse>(
            $"/api/budgets/{budget.BudgetId}/budget-items/{budgetItem.BudgetItemId}");
        var allocation = await server.Client.GetFromJsonAsync<TransactionAllocationResponse>(
            $"/api/transactions/{transaction.TransactionId}/allocation");
        var budgetItems = await server.Client.GetFromJsonAsync<IReadOnlyList<BudgetItemResponse>>(
            $"/api/budgets/{budget.BudgetId}/budget-items");

        Assert.NotNull(retrievedBudgetItem);
        Assert.Equal(budgetItem.BudgetItemId, retrievedBudgetItem.BudgetItemId);
        Assert.NotNull(allocation);
        Assert.Equal(budgetItem.BudgetItemId, allocation.BudgetItemId);
        Assert.NotNull(budgetItems);
        Assert.Equal(budgetItem.BudgetItemId, Assert.Single(budgetItems).BudgetItemId);
    }

    [Fact]
    public async Task Delete_budget_item_returns_not_found_when_allocated_budget_item_belongs_to_another_budget()
    {
        await using var server = await TestApiServer.StartAsync();
        var householdBudget = await CreateBudgetAsync(server, "Household", "GBP");
        var holidayBudget = await CreateBudgetAsync(server, "Holiday", "GBP");
        var groceries = await CreateBudgetItemAsync(server, householdBudget.BudgetId, "Groceries", "Consumption", "400.00");
        var transaction = await CreateTransactionAsync(server, "Groceries", "Debit", "2026-07-02", "42.50", "GBP");
        await AllocateTransactionAsync(server, transaction.TransactionId, groceries.BudgetItemId);

        using var response = await server.Client.DeleteAsync(
            $"/api/budgets/{holidayBudget.BudgetId}/budget-items/{groceries.BudgetItemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var retrievedBudgetItem = await server.Client.GetFromJsonAsync<BudgetItemResponse>(
            $"/api/budgets/{householdBudget.BudgetId}/budget-items/{groceries.BudgetItemId}");

        Assert.NotNull(retrievedBudgetItem);
        Assert.Equal(groceries.BudgetItemId, retrievedBudgetItem.BudgetItemId);
    }

    [Fact]
    public async Task Delete_budget_item_returns_not_found_when_budget_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();

        using var response = await server.Client.DeleteAsync(
            $"/api/budgets/{Guid.NewGuid()}/budget-items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_budget_item_returns_not_found_when_budget_item_does_not_exist()
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.DeleteAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Funding", "3000.00", "name")]
    [InlineData("Salary", "", "3000.00", "kind")]
    [InlineData("Salary", "Income", "3000.00", "kind")]
    [InlineData("Salary", "Funding", "", "plannedAmount")]
    [InlineData("Salary", "Funding", "0.00", "plannedAmount")]
    [InlineData("Salary", "Funding", "-1.00", "plannedAmount")]
    [InlineData("Salary", "Funding", "1.001", "plannedAmount")]
    [InlineData("Salary", "Funding", "100000000.00", "plannedAmount")]
    public async Task Create_budget_item_rejects_invalid_input(
        string name,
        string kind,
        string plannedAmount,
        string expectedErrorKey)
    {
        await using var server = await TestApiServer.StartAsync();
        var budget = await CreateBudgetAsync(server, "UK", "GBP");

        using var response = await server.Client.PostAsJsonAsync(
            $"/api/budgets/{budget.BudgetId}/budget-items",
            new CreateBudgetItemRequest(name, kind, plannedAmount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.Contains(expectedErrorKey, problem.Errors.Keys);
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
        using var response = await server.Client.PostAsJsonAsync(
            "/api/transactions",
            new CreateTransactionRequest(description, type, transactionDate, amount, currency));

        response.EnsureSuccessStatusCode();

        var transaction = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        return transaction ?? throw new InvalidOperationException("Create transaction response was empty.");
    }

    private static async Task AllocateTransactionAsync(TestApiServer server, Guid transactionId, Guid budgetItemId)
    {
        using var response = await server.Client.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/allocation",
            new AllocateTransactionRequest(budgetItemId));

        response.EnsureSuccessStatusCode();
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

    private sealed record TransactionAllocationResponse(
        Guid TransactionId,
        Guid BudgetItemId,
        string Amount,
        string Currency);

    private sealed record ValidationProblemResponse(IDictionary<string, string[]> Errors);
}

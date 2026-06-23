using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class BudgetItemsTests
{
    [Fact]
    public async Task BudgetItemApiCreatesNameOnlyItemsAndHidesLegacyDirectionFields()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);

        var response = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items",
            new CreateBudgetItemRequest("Groceries"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Groceries", json);
        Assert.DoesNotContain("direction", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rolloverType", json, StringComparison.OrdinalIgnoreCase);

        var items = await client.GetFromJsonAsync<IReadOnlyList<BudgetItemDto>>(
            $"/api/budgets/{budget.Id}/budget-items");
        var item = Assert.Single(items!);
        Assert.Equal("Groceries", item.Name);
    }

    [Fact]
    public async Task ArchivedBudgetItemsOnlyAllowRetrospectiveCorrectionsOnOrBeforeArchiveDate()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var retired = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Retired");
        var archiveDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        await BudgetApiTestClient.ArchiveBudgetItem(client, budget.Id, retired.Id);

        var retrospective = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{retired.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(10m, BudgetAdjustmentType.Credit, archiveDate, "Retrospective correction"));
        var future = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/budget-items/{retired.Id}/adjustments",
            new CreateBudgetItemAdjustmentRequest(10m, BudgetAdjustmentType.Credit, archiveDate.AddDays(1), "Future archived activity"));

        Assert.Equal(HttpStatusCode.Created, retrospective.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task ArchivedBudgetItemsRejectFutureTransactionAllocations()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var retired = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Retired");
        var futureDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(1);
        await BudgetApiTestClient.ArchiveBudgetItem(client, budget.Id, retired.Id);
        var transaction = await BudgetApiTestClient.CreateTransaction(client, budget.Id, futureDate, 12m, TransactionDirection.Debit);

        var response = await client.PutAsJsonAsync(
            $"/api/budgets/{budget.Id}/transactions/{transaction.Id}/allocations",
            new ReplaceTransactionAllocationsRequest([new TransactionAllocationItem(retired.Id, 12m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

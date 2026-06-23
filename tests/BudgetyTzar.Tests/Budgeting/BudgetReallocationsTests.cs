using System.Net;
using System.Net.Http.Json;
using BudgetyTzar.Api;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class BudgetReallocationsTests
{
    [Fact]
    public async Task GroupedReallocationRequiresZeroSumAndCreatesLinkedAdjustments()
    {
        await using var app = new BudgetApiFactory();
        var client = app.CreateClient();
        await app.ResetDatabaseAsync();
        var budget = await BudgetApiTestClient.CreateBudget(client);
        var dining = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Dining");
        var groceries = await BudgetApiTestClient.CreateBudgetItem(client, budget.Id, "Groceries");

        var invalid = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 5),
                "Unbalanced",
                [
                    new BudgetReallocationAdjustmentItem(dining.Id, 30m, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(groceries.Id, 20m, BudgetAdjustmentType.Debit)
                ]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await client.PostAsJsonAsync(
            $"/api/budgets/{budget.Id}/reallocations",
            new CreateBudgetItemReallocationRequest(
                new DateOnly(2026, 6, 5),
                "Move budget",
                [
                    new BudgetReallocationAdjustmentItem(dining.Id, 30m, BudgetAdjustmentType.Credit),
                    new BudgetReallocationAdjustmentItem(groceries.Id, 30m, BudgetAdjustmentType.Debit)
                ]));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        var reallocations = await client.GetFromJsonAsync<IReadOnlyList<BudgetReallocationDto>>(
            $"/api/budgets/{budget.Id}/reallocations");
        var reallocation = Assert.Single(reallocations!);
        Assert.Equal(2, reallocation.Adjustments.Count);

        var snapshot = await client.GetFromJsonAsync<BudgetSnapshot>(
            $"/api/budgets/{budget.Id}/snapshot?date=2026-06-05");
        Assert.Equal(-30m, snapshot!.BudgetItems.Single(x => x.BudgetItemId == dining.Id).Balance);
        Assert.Equal(30m, snapshot.BudgetItems.Single(x => x.BudgetItemId == groceries.Id).Balance);
    }
}

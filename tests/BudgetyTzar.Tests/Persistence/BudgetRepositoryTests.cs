using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests.Persistence;

public sealed class BudgetRepositoryTests
{
    [Fact]
    public async Task GetBudgetWithItemsReturnsBudgetNotFoundForMissingBudget()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBudgetRepository>();

        var result = await repository.GetBudgetWithItems(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<BudgetLoadResult.BudgetNotFound>(result);
    }

    [Fact]
    public async Task GetBudgetWithItemsHydratesBudgetOwnedItems()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        var budget = Budget.Create("Personal", "GBP");
        var groceries = BudgetItem.Create(budget.Id, "Groceries", BudgetItemKind.Consumption);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        db.Budgets.Add(budget);
        db.BudgetItems.Add(groceries);
        await db.SaveChangesAsync();

        var result = await repository.GetBudgetWithItems(budget.Id, CancellationToken.None);
        var loaded = Assert.IsType<BudgetLoadResult.Success>(result);

        Assert.Equal(budget.Id, loaded.Budget.Id);
        var item = Assert.Single(loaded.Budget.Items);
        Assert.Equal(groceries.Id, item.Id);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal(Budget.DuplicateBudgetItemNameMessage, loaded.Budget.ValidateBudgetItemName(" Groceries "));
    }
}

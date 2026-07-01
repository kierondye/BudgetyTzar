using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using BudgetyTzar.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests.Persistence;

public sealed class EffectiveBudgetRepositoryTests
{
    [Fact]
    public async Task GetEffectiveBudgetReturnsBudgetNotFoundForMissingBudget()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();

        var result = await repository.GetEffectiveBudget(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 15),
            CancellationToken.None);

        Assert.IsType<EffectiveBudgetLoadResult.BudgetNotFound>(result);
    }

    [Fact]
    public async Task GetEffectiveBudgetLoadsBudgetEvenWhenCommandItemIsUnknown()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        var budget = Budget.Create("Personal", "GBP");
        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        db.Budgets.Add(budget);
        await db.SaveChangesAsync();

        var loadResult = await repository.GetEffectiveBudget(
            budget.Id,
            new DateOnly(2026, 6, 15),
            CancellationToken.None);
        var loaded = Assert.IsType<EffectiveBudgetLoadResult.Success>(loadResult);

        var result = loaded.Budget.RecordAdjustment(
            Guid.NewGuid(),
            Money(10m),
            BudgetAdjustmentType.Credit,
            "Unknown item");

        Assert.IsType<EffectiveBudgetResult.ItemNotFound>(result);
    }

    [Fact]
    public async Task GetEffectiveBudgetHydratesDateScopedPlannedAmounts()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        var budget = Budget.Create("Personal", "GBP");
        var salary = BudgetItem.Create(budget.Id, "Salary", BudgetItemKind.Funding);
        var groceries = BudgetItem.Create(budget.Id, "Groceries", BudgetItemKind.Consumption);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
        db.Budgets.Add(budget);
        db.BudgetItems.AddRange(salary, groceries);
        db.BudgetAdjustments.AddRange(
            BudgetAdjustment.Create(
                budget.Id,
                salary.Id,
                100m,
                BudgetAdjustmentType.Credit,
                new DateOnly(2026, 6, 1),
                "Current income"),
            BudgetAdjustment.Create(
                budget.Id,
                groceries.Id,
                75m,
                BudgetAdjustmentType.Debit,
                new DateOnly(2026, 6, 2),
                "Current spending"),
            BudgetAdjustment.Create(
                budget.Id,
                salary.Id,
                100m,
                BudgetAdjustmentType.Credit,
                new DateOnly(2026, 7, 1),
                "Future income"));
        await db.SaveChangesAsync();

        var loadResult = await repository.GetEffectiveBudget(
            budget.Id,
            new DateOnly(2026, 6, 15),
            CancellationToken.None);
        var loaded = Assert.IsType<EffectiveBudgetLoadResult.Success>(loadResult);

        var salaryCorrection = loaded.Budget.RecordAdjustment(
            salary.Id,
            Money(25m),
            BudgetAdjustmentType.Debit,
            "Reduced current income");
        Assert.IsType<EffectiveBudgetResult.Success>(salaryCorrection);

        var result = loaded.Budget.RecordAdjustment(
            groceries.Id,
            Money(25.01m),
            BudgetAdjustmentType.Debit,
            "Exceeds current funding");

        var validationProblem = Assert.IsType<EffectiveBudgetResult.ValidationFailed>(result);
        Assert.Equal(EffectiveBudget.NetPlannedSpendingExceededMessage, validationProblem.Error);
    }

    [Fact]
    public async Task SavePersistsAllPendingAdjustmentsAndEvents()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        var budgetId = Guid.NewGuid();
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            new DateOnly(2026, 6, 15),
            100m,
            [new EffectiveBudgetItemState(groceries, 0m)]);

        var firstResult = Assert.IsType<EffectiveBudgetResult.Success>(
            effectiveBudget.RecordAdjustment(groceries.Id, Money(25m), BudgetAdjustmentType.Debit, "First change"));
        var secondResult = Assert.IsType<EffectiveBudgetResult.Success>(
            firstResult.Budget.RecordAdjustment(groceries.Id, Money(30m), BudgetAdjustmentType.Debit, "Second change"));

        var modifiedBudget = secondResult.Budget;

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();

        var saved = await repository.Save(modifiedBudget, CancellationToken.None);

        Assert.Equal(2, saved.EventIds.Count);
        Assert.Equal(2, await db.BudgetAdjustments.CountAsync());
        var outboxEventIds = await db.OutboxMessages
            .Where(x => x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1")
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, outboxEventIds.Count);
        Assert.All(saved.EventIds, eventId => Assert.Contains(eventId, outboxEventIds));
    }

    [Fact]
    public async Task SavePersistsPendingReallocationsLinkedAdjustmentsAndEvents()
    {
        await using var app = new BudgetApiFactory();
        await app.ResetDatabaseAsync();

        var budgetId = Guid.NewGuid();
        var dining = BudgetItem.Create(budgetId, "Dining", BudgetItemKind.Consumption);
        var groceries = BudgetItem.Create(budgetId, "Groceries", BudgetItemKind.Consumption);
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            new DateOnly(2026, 6, 15),
            0m,
            [
                new EffectiveBudgetItemState(dining, 0m),
                new EffectiveBudgetItemState(groceries, 0m)
            ]);

        var result = Assert.IsType<EffectiveBudgetResult.Success>(
            effectiveBudget.RecordReallocation(
                [
                    new EffectiveBudgetReallocationAdjustment(dining.Id, Money(25m), BudgetAdjustmentType.Credit),
                    new EffectiveBudgetReallocationAdjustment(groceries.Id, Money(25m), BudgetAdjustmentType.Debit)
                ],
                "Move budget"));
        var modifiedBudget = result.Budget;

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();

        var saved = await repository.Save(modifiedBudget, CancellationToken.None);

        var eventId = Assert.Single(saved.EventIds);
        Assert.Equal(1, await db.BudgetReallocations.CountAsync());
        Assert.Equal(2, await db.BudgetAdjustments.CountAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x =>
            x.Id == eventId &&
            x.EventType == "budgetytzar.budgeting.budget-reallocation-recorded.v1"));
    }

    private static PositiveMoneyAmount Money(decimal amount) =>
        PositiveMoneyAmount.Require(amount);
}

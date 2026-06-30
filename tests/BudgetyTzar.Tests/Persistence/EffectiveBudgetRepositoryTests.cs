using BudgetyTzar.Api;
using BudgetyTzar.Api.Infrastructure.Persistence;
using BudgetyTzar.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetyTzar.Tests.Persistence;

public sealed class EffectiveBudgetRepositoryTests
{
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
            effectiveBudget.RecordAdjustment(groceries.Id, 25m, BudgetAdjustmentType.Debit, "First change"));
        Assert.IsType<EffectiveBudgetResult.Success>(
            firstResult.Budget.RecordAdjustment(groceries.Id, 30m, BudgetAdjustmentType.Debit, "Second change"));

        var firstPendingAdjustment = effectiveBudget.PendingAdjustments.First();

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEffectiveBudgetRepository>();
        var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();

        var saved = await repository.Save(effectiveBudget, CancellationToken.None);

        Assert.Equal(firstPendingAdjustment.Id, saved.CreatedAdjustment.Id);
        Assert.NotNull(saved.EventId);
        Assert.Equal(2, await db.BudgetAdjustments.CountAsync());
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x =>
            x.EventType == "budgetytzar.budgeting.budget-adjustment-recorded.v1"));
    }
}

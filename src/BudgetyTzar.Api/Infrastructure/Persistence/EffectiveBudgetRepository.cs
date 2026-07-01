using BudgetyTzar.Api.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class EffectiveBudgetRepository(
    BudgetDbContext db,
    DomainEventOutboxWriter events) : IEffectiveBudgetRepository
{
    public async Task<EffectiveBudgetLoadResult> GetEffectiveBudget(
        Guid budgetId,
        DateOnly date,
        CancellationToken ct)
    {
        var budgetExists = await db.Budgets
            .AsNoTracking()
            .AnyAsync(x => x.Id == budgetId, ct);
        if (!budgetExists)
        {
            return new EffectiveBudgetLoadResult.BudgetNotFound();
        }

        var budgetItems = await db.BudgetItems
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);

        var effectivePlannedAmounts = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date <= date)
            .GroupBy(x => x.BudgetItemId)
            .Select(x => new EffectivePlannedAmount(
                x.Key,
                x.Sum(y => y.Type == BudgetAdjustmentType.Credit ? y.Amount : -y.Amount)))
            .ToListAsync(ct);

        var effectivePlannedAmountsByItemId = effectivePlannedAmounts.ToDictionary(
            x => x.BudgetItemId,
            x => x.PlannedAmount);
        var effectiveBudgetItems = budgetItems
            .Select(x => new EffectiveBudgetItemState(
                x,
                effectivePlannedAmountsByItemId.GetValueOrDefault(x.Id)))
            .ToArray();
        var effectiveBudget = new EffectiveBudget(
            budgetId,
            date,
            effectivePlannedAmounts.Sum(x => x.PlannedAmount),
            effectiveBudgetItems);

        return new EffectiveBudgetLoadResult.Success(effectiveBudget);
    }

    public async Task<EffectiveBudgetSaveResult> Save(EffectiveBudget budget, CancellationToken ct)
    {
        if (budget.PendingAdjustments.Count == 0 && budget.PendingReallocations.Count == 0)
        {
            throw new InvalidOperationException(
                "An effective budget save requires at least one pending adjustment or reallocation.");
        }

        db.BudgetReallocations.AddRange(budget.PendingReallocations);
        db.BudgetAdjustments.AddRange(budget.PendingAdjustments);

        var eventIds = budget.PendingEvents
            .Select(events.Add)
            .ToArray();

        await db.SaveChangesAsync(ct);

        return new EffectiveBudgetSaveResult(eventIds);
    }
}

internal sealed record EffectivePlannedAmount(Guid BudgetItemId, decimal PlannedAmount);

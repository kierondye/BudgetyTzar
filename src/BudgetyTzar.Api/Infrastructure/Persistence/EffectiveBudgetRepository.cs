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
        var createdAdjustment = budget.PendingAdjustments.FirstOrDefault()
            ?? throw new InvalidOperationException("An effective budget save requires at least one pending adjustment.");

        db.BudgetAdjustments.AddRange(budget.PendingAdjustments);

        Guid? firstEventId = null;
        foreach (var domainEvent in budget.PendingEvents)
        {
            var eventId = events.Add(domainEvent);
            firstEventId ??= eventId;
        }

        await db.SaveChangesAsync(ct);

        return new EffectiveBudgetSaveResult(createdAdjustment, firstEventId);
    }
}

internal sealed record EffectivePlannedAmount(Guid BudgetItemId, decimal PlannedAmount);

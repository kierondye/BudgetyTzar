using BudgetyTzar.Api.Infrastructure.Events;

namespace BudgetyTzar.Api.Infrastructure.Persistence;

public sealed class EffectiveBudgetRepository(
    BudgetDbContext db,
    DomainEventOutboxWriter events) : IEffectiveBudgetRepository
{
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

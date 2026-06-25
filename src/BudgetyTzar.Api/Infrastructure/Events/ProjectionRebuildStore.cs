using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class ProjectionRebuildStore(BudgetDbContext db, EventSchemaValidator schemaValidator)
{
    public async Task ResetAll(CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections);
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections);
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents);
        db.BudgetItemProjectionStates.RemoveRange(db.BudgetItemProjectionStates);
        db.BudgetAdjustmentProjectionStates.RemoveRange(db.BudgetAdjustmentProjectionStates);
        db.TransactionAllocationProjectionStates.RemoveRange(db.TransactionAllocationProjectionStates);
        db.TransactionProjectionStates.RemoveRange(db.TransactionProjectionStates);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetBudget(Guid budgetId, CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId));
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents.Where(x => x.BudgetId == budgetId));
        db.BudgetItemProjectionStates.RemoveRange(db.BudgetItemProjectionStates.Where(x => x.BudgetId == budgetId));
        db.BudgetAdjustmentProjectionStates.RemoveRange(db.BudgetAdjustmentProjectionStates.Where(x => x.BudgetId == budgetId));
        db.TransactionAllocationProjectionStates.RemoveRange(db.TransactionAllocationProjectionStates.Where(x => x.BudgetId == budgetId));
        db.TransactionProjectionStates.RemoveRange(db.TransactionProjectionStates.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EventEnvelope>> LoadOutboxEnvelopes(Guid? budgetId, CancellationToken ct)
    {
        var query = db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.BudgetId != null);
        if (budgetId.HasValue)
        {
            query = query.Where(x => x.BudgetId == budgetId.Value);
        }

        var messages = await query
            .Select(x => new { x.CreatedAt, x.EnvelopeJson })
            .ToListAsync(ct);

        return messages
            .OrderBy(x => x.CreatedAt)
            .Select(x => schemaValidator.ValidateAndDeserialize(x.EnvelopeJson))
            .ToList();
    }
}

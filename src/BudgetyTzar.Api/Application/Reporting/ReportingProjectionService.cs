using System.Text.Json;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed class ReportingProjectionService(BudgetDbContext db)
{
    public async Task RebuildFromOutbox(CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections);
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections);
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines);
        db.ProcessedProjectionEvents.RemoveRange(db.ProcessedProjectionEvents);
        await db.SaveChangesAsync(ct);

        var budgetIds = await db.OutboxMessages
            .Where(x => x.BudgetId.HasValue)
            .Select(x => x.BudgetId!.Value)
            .Distinct()
            .ToListAsync(ct);

        foreach (var budgetId in budgetIds)
        {
            await RebuildBudget(budgetId, ct);
        }
    }

    public async Task ProjectEnvelope(string envelopeJson, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(envelopeJson, EventSerialization.Options);
        if (envelope is null
            || await db.ProcessedProjectionEvents.AnyAsync(x => x.EventId == envelope.EventId, ct))
        {
            return;
        }

        Guid? budgetId = null;
        if (envelope.Payload.TryGetPropertyValue("budgetId", out var budgetIdNode)
            && budgetIdNode is not null)
        {
            budgetId = budgetIdNode.GetValue<Guid>();
            await RebuildBudget(budgetId.Value, ct);
        }

        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            BudgetId = budgetId,
            OccurredAt = envelope.OccurredAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RebuildBudget(Guid budgetId, CancellationToken ct)
    {
        db.BudgetSnapshotItemProjections.RemoveRange(db.BudgetSnapshotItemProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetSnapshotProjections.RemoveRange(db.BudgetSnapshotProjections.Where(x => x.BudgetId == budgetId));
        db.BudgetAuditTimelines.RemoveRange(db.BudgetAuditTimelines.Where(x => x.BudgetId == budgetId));
        await db.SaveChangesAsync(ct);

        var adjustmentDates = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .Select(x => x.Date)
            .ToListAsync(ct);
        var transactionDates = await db.Transactions
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .Select(x => x.TransactionDate)
            .ToListAsync(ct);
        var dates = adjustmentDates
            .Concat(transactionDates)
            .Distinct()
            .Order()
            .ToList();

        foreach (var date in dates)
        {
            var snapshot = await LedgerSnapshotCalculator.Calculate(db, budgetId, date, ct);
            if (snapshot is null)
            {
                continue;
            }

            var projection = new BudgetSnapshotProjection
            {
                BudgetId = budgetId,
                Date = date,
                UnbudgetedBalance = snapshot.UnbudgetedBalance,
                TotalBalance = snapshot.TotalBalance
            };
            db.BudgetSnapshotProjections.Add(projection);
            db.BudgetSnapshotItemProjections.AddRange(snapshot.BudgetItems.Select(item => new BudgetSnapshotItemProjection
            {
                SnapshotId = projection.Id,
                BudgetId = budgetId,
                Date = date,
                BudgetItemId = item.BudgetItemId,
                Name = item.Name,
                Balance = item.Balance
            }));
        }

        await ProjectAuditTimeline(budgetId, ct);
        var projectedAt = DateTimeOffset.UtcNow;
        await db.OutboxMessages
            .Where(x => x.BudgetId == budgetId && x.ProjectedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectedAt, projectedAt), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ProjectAuditTimeline(Guid budgetId, CancellationToken ct)
    {
        var audits = await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId)
            .ToListAsync(ct);

        db.BudgetAuditTimelines.AddRange(audits.Select(audit => new BudgetAuditTimelineProjection
        {
            AuditEventId = audit.Id,
            BudgetId = audit.BudgetId,
            OccurredAt = audit.OccurredAt,
            EventType = audit.EventType,
            EntityType = audit.EntityType,
            EntityId = audit.EntityId,
            Description = audit.Description,
            Details = audit.Details
        }));
    }
}

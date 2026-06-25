using System.Text.Json;
using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class ProjectionProcessingStore(BudgetDbContext db)
{
    public async Task<bool> TryClaim(
        EventEnvelope envelope,
        Guid processingInstanceId,
        int processingLeaseSeconds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var leaseCutoff = now.AddSeconds(-Math.Max(1, processingLeaseSeconds));
        var budgetId = ReadBudgetId(envelope);
        var projectionEvent = await db.ProcessedProjectionEvents
            .SingleOrDefaultAsync(x => x.EventId == envelope.EventId, ct);
        if (projectionEvent is not null)
        {
            return await TryClaimExisting(
                projectionEvent,
                envelope,
                budgetId,
                processingInstanceId,
                leaseCutoff,
                now,
                ct);
        }

        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            BudgetId = budgetId,
            OccurredAt = envelope.OccurredAt,
            ProcessedAt = now,
            Status = ProjectionProcessingStatus.Processing,
            ProcessingInstanceId = processingInstanceId,
            ProcessingStartedAt = now,
            ProcessingUpdatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            projectionEvent = await db.ProcessedProjectionEvents
                .SingleOrDefaultAsync(x => x.EventId == envelope.EventId, ct);
            return projectionEvent is not null
                && await TryClaimExisting(
                    projectionEvent,
                    envelope,
                    budgetId,
                    processingInstanceId,
                    leaseCutoff,
                    now,
                    ct);
        }
    }

    public Task<bool> IsCompleted(Guid eventId, CancellationToken ct) =>
        db.ProcessedProjectionEvents
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.Status == ProjectionProcessingStatus.Completed, ct);

    public async Task MarkCompleted(
        EventEnvelope envelope,
        ProjectionApplyResult result,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        var projectionEvent = await db.ProcessedProjectionEvents.SingleAsync(x => x.EventId == envelope.EventId, ct);
        projectionEvent.EventType = envelope.EventType;
        projectionEvent.BudgetId = result.BudgetId;
        projectionEvent.OccurredAt = envelope.OccurredAt;
        projectionEvent.ProcessedAt = projectedAt;
        projectionEvent.Status = ProjectionProcessingStatus.Completed;
        projectionEvent.ProcessingUpdatedAt = projectedAt;
        projectionEvent.CompletedAt = projectedAt;
        projectionEvent.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailed(Guid eventId, Exception exception, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var projectionEvent = await db.ProcessedProjectionEvents.SingleOrDefaultAsync(x => x.EventId == eventId, ct);
        if (projectionEvent is null)
        {
            return;
        }

        projectionEvent.Status = ProjectionProcessingStatus.Failed;
        projectionEvent.ProcessingUpdatedAt = now;
        projectionEvent.LastError = Truncate(exception.Message, 4000);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> TryClaimExisting(
        ProcessedProjectionEvent projectionEvent,
        EventEnvelope envelope,
        Guid budgetId,
        Guid processingInstanceId,
        DateTimeOffset leaseCutoff,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (projectionEvent.Status == ProjectionProcessingStatus.Completed)
        {
            return false;
        }

        if (projectionEvent.Status == ProjectionProcessingStatus.Processing
            && projectionEvent.ProcessingUpdatedAt >= leaseCutoff)
        {
            return false;
        }

        projectionEvent.EventType = envelope.EventType;
        projectionEvent.BudgetId = budgetId;
        projectionEvent.OccurredAt = envelope.OccurredAt;
        projectionEvent.Status = ProjectionProcessingStatus.Processing;
        projectionEvent.ProcessingInstanceId = processingInstanceId;
        projectionEvent.ProcessingStartedAt = now;
        projectionEvent.ProcessingUpdatedAt = now;
        projectionEvent.LastError = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Guid ReadBudgetId(EventEnvelope envelope)
    {
        if (envelope.Payload["budgetId"] is { } node
            && node.Deserialize<Guid?>(EventSerialization.Options) is { } budgetId)
        {
            return budgetId;
        }

        throw new PermanentProjectionException($"Event payload for '{envelope.EventType}' is missing required budgetId.");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

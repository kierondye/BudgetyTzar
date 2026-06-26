using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class AuditFailureStore(BudgetDbContext db)
{
    public async Task Persist(
        ConsumeResult<string, string> result,
        string groupId,
        AuditFailureCategory category,
        AuditFailureStatus status,
        int retryCount,
        Exception exception,
        CancellationToken ct)
    {
        var metadata = FailureEventMetadataReader.Read(result.Message.Value);
        var now = DateTimeOffset.UtcNow;
        var existing = await db.AuditEventFailures
            .FirstOrDefaultAsync(x => x.Topic == result.Topic && x.Partition == result.Partition.Value && x.Offset == result.Offset.Value, ct);

        if (existing is null)
        {
            db.AuditEventFailures.Add(new AuditEventFailure
            {
                EventId = metadata.EventId,
                Topic = result.Topic,
                Partition = result.Partition.Value,
                Offset = result.Offset.Value,
                ConsumerGroup = groupId,
                EventType = metadata.EventType,
                BudgetId = metadata.BudgetId,
                Category = category,
                Status = status,
                RetryCount = retryCount,
                LastError = FailureEventMetadataReader.Truncate(exception.Message, 4000),
                RawEventJson = result.Message.Value,
                FirstFailedAt = now,
                LastFailedAt = now
            });
        }
        else
        {
            existing.EventId = metadata.EventId ?? existing.EventId;
            existing.EventType = metadata.EventType ?? existing.EventType;
            existing.BudgetId = metadata.BudgetId ?? existing.BudgetId;
            existing.Category = category;
            existing.Status = status;
            existing.RetryCount = retryCount;
            existing.LastError = FailureEventMetadataReader.Truncate(exception.Message, 4000);
            existing.RawEventJson = result.Message.Value;
            existing.LastFailedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public static Guid? TryReadEventId(string eventJson) => FailureEventMetadataReader.TryReadEventId(eventJson);
}

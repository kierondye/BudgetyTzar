using System.Text.Json;
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
        var metadata = TryReadMetadata(result.Message.Value);
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
                LastError = Truncate(exception.Message, 4000),
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
            existing.LastError = Truncate(exception.Message, 4000);
            existing.RawEventJson = result.Message.Value;
            existing.LastFailedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public static Guid? TryReadEventId(string eventJson) => TryReadMetadata(eventJson).EventId;

    private static (Guid? EventId, string? EventType, Guid? BudgetId) TryReadMetadata(string eventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(eventJson);
            var root = document.RootElement;
            var eventId = root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.TryGetGuid(out var parsedEventId)
                ? parsedEventId
                : (Guid?)null;
            var eventType = root.TryGetProperty("eventType", out var eventTypeElement)
                ? eventTypeElement.GetString()
                : null;
            Guid? budgetId = null;
            if (root.TryGetProperty("payload", out var payload)
                && payload.TryGetProperty("budgetId", out var budgetIdElement)
                && budgetIdElement.TryGetGuid(out var parsedBudgetId))
            {
                budgetId = parsedBudgetId;
            }

            return (eventId, eventType, budgetId);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

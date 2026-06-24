using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetyTzar.Api.Application.Reporting;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class AuditEventWriter(BudgetDbContext db, IOptions<EventTopicOptions> topics)
{
    public Guid Add(DomainEvent domainEvent)
    {
        var occurredAt = domainEvent.OccurredAt ?? DateTimeOffset.UtcNow;
        var audit = new AuditEvent
        {
            OccurredAt = occurredAt,
            BudgetId = domainEvent.BudgetId,
            AppliesToAllPeriods = domainEvent.AppliesToAllPeriods,
            EntityType = domainEvent.EntityType,
            EntityId = domainEvent.EntityId,
            EventType = domainEvent.EventType,
            Description = domainEvent.Description,
            Details = domainEvent.Details
        };
        db.AuditEvents.Add(audit);

        var canonicalEventType = EventTypes.ToCanonical(domainEvent.EventType);
        var outboxId = Guid.NewGuid();
        var envelope = new EventEnvelope<JsonObject>(
            outboxId,
            canonicalEventType,
            occurredAt,
            Guid.NewGuid(),
            null,
            domainEvent.EntityId,
            domainEvent.EntityType,
            1,
            CreatePayload(domainEvent));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            Topic = EventTypes.ToTopic(canonicalEventType, topics.Value),
            EventType = canonicalEventType,
            AggregateId = domainEvent.EntityId,
            AggregateType = domainEvent.EntityType,
            BudgetId = domainEvent.BudgetId,
            EnvelopeJson = JsonSerializer.Serialize(envelope, EventSerialization.Options)
        });
        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = outboxId,
            EventType = canonicalEventType,
            BudgetId = domainEvent.BudgetId,
            OccurredAt = occurredAt,
            ProcessedAt = occurredAt,
            Status = ProjectionProcessingStatus.Pending
        });

        return outboxId;
    }

    private static JsonObject CreatePayload(DomainEvent domainEvent)
    {
        var payload = domainEvent.Payload is null
            ? JsonSerializer.SerializeToNode(CanonicalEventPayload.From(domainEvent), EventSerialization.Options)!.AsObject()
            : JsonSerializer.SerializeToNode(domainEvent.Payload, EventSerialization.Options)!.AsObject();
        payload["budgetId"] = domainEvent.BudgetId;
        return payload;
    }
}

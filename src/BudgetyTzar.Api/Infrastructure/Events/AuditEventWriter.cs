using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class AuditEventWriter(BudgetDbContext db, IOptions<EventTopicOptions> topics)
{
    public void Add(DomainEvent domainEvent)
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
            CreatePayload(domainEvent, audit.Id));

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
    }

    private static JsonObject CreatePayload(DomainEvent domainEvent, Guid auditEventId)
    {
        var payload = domainEvent.Payload is null
            ? JsonSerializer.SerializeToNode(CanonicalEventPayload.From(domainEvent, auditEventId), EventSerialization.Options)!.AsObject()
            : JsonSerializer.SerializeToNode(domainEvent.Payload, EventSerialization.Options)!.AsObject();
        payload["auditEventId"] = auditEventId;
        payload["auditEventType"] = domainEvent.EventType;
        payload["auditDescription"] = domainEvent.Description;
        payload["auditDetails"] = domainEvent.Details;
        payload["budgetId"] = domainEvent.BudgetId;
        return payload;
    }
}

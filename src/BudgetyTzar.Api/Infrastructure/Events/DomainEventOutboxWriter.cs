using BudgetyTzar.Api.Application.Reporting;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BudgetyTzar.Api.Infrastructure.Events;

public sealed class DomainEventOutboxWriter(BudgetDbContext db, IOptions<EventTopicOptions> topics)
{
    public Guid Add(DomainEvent domainEvent)
    {
        var occurredAt = domainEvent.OccurredAt ?? DateTimeOffset.UtcNow;
        var canonicalEventType = EventTypes.ToCanonical(domainEvent.EventType);
        var eventId = Guid.NewGuid();
        var envelope = new EventEnvelope<JsonObject>(
            eventId,
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
            Id = eventId,
            Topic = EventTypes.ToTopic(canonicalEventType, topics.Value),
            EventType = canonicalEventType,
            AggregateId = domainEvent.EntityId,
            AggregateType = domainEvent.EntityType,
            BudgetId = domainEvent.BudgetId,
            EnvelopeJson = JsonSerializer.Serialize(envelope, EventSerialization.Options)
        });
        db.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            EventType = canonicalEventType,
            BudgetId = domainEvent.BudgetId,
            OccurredAt = occurredAt,
            ProcessedAt = occurredAt,
            Status = ProjectionProcessingStatus.Pending
        });

        return eventId;
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

namespace BudgetyTzar.Api;

public sealed record CanonicalEventPayload(
    Guid AuditEventId,
    Guid BudgetId,
    string EntityType,
    Guid EntityId,
    string EventName,
    bool AppliesToAllPeriods)
{
    public static CanonicalEventPayload From(DomainEvent domainEvent, Guid auditEventId) =>
        new(
            auditEventId,
            domainEvent.BudgetId,
            domainEvent.EntityType,
            domainEvent.EntityId,
            domainEvent.EventType,
            domainEvent.AppliesToAllPeriods);
}

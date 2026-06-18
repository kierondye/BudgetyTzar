namespace BudgetyTzar.Api;

public sealed record CanonicalEventPayload(
    Guid AuditEventId,
    Guid BudgetId,
    Guid? BudgetPeriodId,
    string EntityType,
    Guid EntityId,
    string EventName,
    string Description,
    string? Details,
    bool AppliesToAllPeriods)
{
    public static CanonicalEventPayload From(DomainEvent domainEvent, Guid auditEventId) =>
        new(
            auditEventId,
            domainEvent.BudgetId,
            domainEvent.BudgetPeriodId,
            domainEvent.EntityType,
            domainEvent.EntityId,
            domainEvent.EventType,
            domainEvent.Description,
            domainEvent.Details,
            domainEvent.AppliesToAllPeriods);
}

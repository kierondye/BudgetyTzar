namespace BudgetyTzar.Api;

public sealed record CanonicalEventPayload(
    Guid BudgetId,
    string EntityType,
    Guid EntityId,
    string EventName,
    bool AppliesToAllPeriods)
{
    public static CanonicalEventPayload From(DomainEvent domainEvent) =>
        new(
            domainEvent.BudgetId,
            domainEvent.EntityType,
            domainEvent.EntityId,
            domainEvent.EventType,
            domainEvent.AppliesToAllPeriods);
}

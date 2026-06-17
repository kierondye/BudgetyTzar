namespace BudgetyTzar.Api;

public sealed record DomainEvent(
    string EventType,
    Guid BudgetId,
    Guid? BudgetPeriodId,
    string EntityType,
    Guid EntityId,
    string Description,
    string? Details = null,
    bool AppliesToAllPeriods = false,
    DateTimeOffset? OccurredAt = null);

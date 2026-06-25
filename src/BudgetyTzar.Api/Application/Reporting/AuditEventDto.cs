namespace BudgetyTzar.Api.Application.Reporting;

public sealed record AuditEventDto(
    Guid Id,
    Guid BudgetId,
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid EntityId,
    string Description,
    string? Details);

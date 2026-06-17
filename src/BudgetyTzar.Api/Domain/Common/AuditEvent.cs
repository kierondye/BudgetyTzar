namespace BudgetyTzar.Api;

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid? BudgetPeriodId { get; set; }
    public bool AppliesToAllPeriods { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string EventType { get; set; }
    public required string Description { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

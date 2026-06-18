namespace BudgetyTzar.Api.Application.Reporting;

public sealed class BudgetSnapshotProjection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public DateOnly Date { get; set; }
    public decimal UnbudgetedBalance { get; set; }
    public decimal TotalBalance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetSnapshotItemProjection
{
    public Guid SnapshotId { get; set; }
    public Guid BudgetItemId { get; set; }
    public Guid BudgetId { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetAuditTimelineProjection
{
    public Guid AuditEventId { get; set; }
    public Guid BudgetId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Description { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProcessedProjectionEvent
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public Guid? BudgetId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

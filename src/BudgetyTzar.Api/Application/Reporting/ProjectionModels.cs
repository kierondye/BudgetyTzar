namespace BudgetyTzar.Api.Application.Reporting;

public sealed class BudgetSnapshotProjection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public DateOnly Date { get; set; }
    public decimal UnbudgetedBalance { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal TotalTransactionBalance { get; set; }
    public decimal TotalBudgetedBalance { get; set; }
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
    public decimal PlannedCredit { get; set; }
    public decimal PlannedDebit { get; set; }
    public decimal ActualCredit { get; set; }
    public decimal ActualDebit { get; set; }
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

public sealed class BudgetItemProjectionState
{
    public Guid BudgetItemId { get; set; }
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetAdjustmentProjectionState
{
    public Guid ActivityId { get; set; }
    public Guid BudgetId { get; set; }
    public Guid BudgetItemId { get; set; }
    public Guid SourceEventId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public BudgetAdjustmentType Direction { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TransactionProjectionState
{
    public Guid TransactionId { get; set; }
    public Guid BudgetId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public bool IsIgnored { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TransactionAllocationProjectionState
{
    public Guid TransactionId { get; set; }
    public Guid BudgetItemId { get; set; }
    public Guid BudgetId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ProjectionFailureCategory
{
    Validation,
    Projection,
    DeadLetterPublish
}

public enum ProjectionFailureStatus
{
    Pending,
    DeadLettered,
    Retryable
}

public sealed class ProjectionEventFailure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? EventId { get; set; }
    public required string Topic { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public required string ConsumerGroup { get; set; }
    public string? EventType { get; set; }
    public Guid? BudgetId { get; set; }
    public ProjectionFailureCategory Category { get; set; }
    public ProjectionFailureStatus Status { get; set; }
    public int RetryCount { get; set; }
    public required string LastError { get; set; }
    public required string RawEventJson { get; set; }
    public DateTimeOffset FirstFailedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastFailedAt { get; set; } = DateTimeOffset.UtcNow;
}

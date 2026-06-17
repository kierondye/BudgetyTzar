namespace BudgetyTzar.Api;

public enum BudgetLineDirection
{
    Debit,
    Credit
}

public enum BudgetLineRolloverType
{
    PeriodReset,
    Cumulative
}

public enum TransactionDirection
{
    Debit,
    Credit
}

public enum TransactionAssignmentStatus
{
    Unassigned,
    PartiallyAssigned,
    FullyAssigned
}

public enum TransactionImportBatchStatus
{
    Previewed,
    Committed
}

public sealed class Budget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetPeriod
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public BudgetLineDirection Direction { get; set; }
    public BudgetLineRolloverType RolloverType { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetLineAllocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class FinancialTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public Guid? ImportBatchId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? SourceAccount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsIgnored { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TransactionAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetReallocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid FromBudgetLineId { get; set; }
    public Guid ToBudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetAdjustment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

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

public sealed class TransactionImportBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public required string FileName { get; set; }
    public TransactionImportBatchStatus Status { get; set; } = TransactionImportBatchStatus.Previewed;
    public int RowCount { get; set; }
    public int DuplicateCandidateCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CommittedAt { get; set; }
}

public sealed class TransactionImportRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public DateOnly TransactionDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? SourceAccount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsDuplicateCandidate { get; set; }
    public string? DuplicateReason { get; set; }
    public bool IsCommitted { get; set; }
    public Guid? TransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

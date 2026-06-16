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
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class FinancialTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
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
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetReallocation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BudgetPeriodId { get; set; }
    public Guid FromBudgetLineId { get; set; }
    public Guid ToBudgetLineId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

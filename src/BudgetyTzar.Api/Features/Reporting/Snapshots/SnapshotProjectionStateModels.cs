namespace BudgetyTzar.Api.Application.Reporting;

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

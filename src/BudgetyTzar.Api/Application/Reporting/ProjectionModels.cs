namespace BudgetyTzar.Api.Application.Reporting;

public sealed class PeriodBudgetSummaryProjection
{
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetId { get; set; }
    public required string PeriodName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PlannedDebit { get; set; }
    public decimal ActualDebit { get; set; }
    public decimal DebitRemaining { get; set; }
    public decimal DebitVariance { get; set; }
    public decimal PlannedCredit { get; set; }
    public decimal ActualCredit { get; set; }
    public decimal CreditVariance { get; set; }
    public decimal UnassignedDebitTotal { get; set; }
    public decimal UnassignedCreditTotal { get; set; }
    public decimal PartiallyAssignedDebitTotal { get; set; }
    public decimal PartiallyAssignedCreditTotal { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetLinePeriodSummaryProjection
{
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public Guid BudgetId { get; set; }
    public required string Name { get; set; }
    public BudgetLineDirection Direction { get; set; }
    public BudgetLineRolloverType RolloverType { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Allocated { get; set; }
    public decimal ReallocationIn { get; set; }
    public decimal ReallocationOut { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal ClosingBalance { get; set; }
    public bool IsOverBudget { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CreditBudgetLinePeriodSummaryProjection
{
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public Guid BudgetId { get; set; }
    public required string PeriodName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public required string BudgetLineName { get; set; }
    public decimal PlannedCredit { get; set; }
    public decimal ActualCredit { get; set; }
    public decimal CreditVariance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TransactionAssignmentSummaryProjection
{
    public Guid TransactionId { get; set; }
    public Guid BudgetId { get; set; }
    public Guid? BudgetPeriodId { get; set; }
    public decimal TransactionAmount { get; set; }
    public decimal AssignedAmount { get; set; }
    public decimal UnassignedAmount { get; set; }
    public TransactionDirection Direction { get; set; }
    public bool IsIgnored { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CumulativeBudgetLineBalanceProjection
{
    public Guid BudgetPeriodId { get; set; }
    public Guid BudgetLineId { get; set; }
    public Guid BudgetId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BudgetAuditTimelineProjection
{
    public Guid AuditEventId { get; set; }
    public Guid BudgetId { get; set; }
    public Guid? BudgetPeriodId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Description { get; set; }
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

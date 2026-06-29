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
    public BudgetItemKind Kind { get; set; }
    public decimal Balance { get; set; }
    public decimal PlannedCredit { get; set; }
    public decimal PlannedDebit { get; set; }
    public decimal ActualCredit { get; set; }
    public decimal ActualDebit { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

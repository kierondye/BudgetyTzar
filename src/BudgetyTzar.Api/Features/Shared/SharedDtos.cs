namespace BudgetyTzar.Api;

public sealed record TransactionAllocationItem(Guid BudgetItemId, decimal Amount);
public sealed record BudgetReallocationAdjustmentItem(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction);

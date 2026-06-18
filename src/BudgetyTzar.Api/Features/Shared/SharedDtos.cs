namespace BudgetyTzar.Api;

public sealed record TransactionAssignmentItem(Guid BudgetLineId, decimal Amount);
public sealed record BudgetReallocationAdjustmentItem(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction);

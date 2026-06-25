namespace BudgetyTzar.Api;

public sealed record BudgetReallocationAdjustmentItem(Guid BudgetItemId, decimal Amount, BudgetAdjustmentType Direction);

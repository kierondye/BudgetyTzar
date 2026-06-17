namespace BudgetyTzar.Api;

public sealed record BudgetLineAllocationItem(Guid BudgetLineId, decimal Amount);
public sealed record TransactionAssignmentItem(Guid BudgetLineId, decimal Amount);

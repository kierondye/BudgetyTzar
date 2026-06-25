namespace BudgetyTzar.Api;

public sealed record TransactionAllocationItem(Guid BudgetItemId, decimal Amount, string? Notes = null);

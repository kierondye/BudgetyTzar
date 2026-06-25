namespace BudgetyTzar.Api.Application.Reporting;

public sealed record BudgetSnapshot(
    Guid BudgetId,
    DateOnly Date,
    decimal UnbudgetedBalance,
    decimal TotalBalance,
    decimal TotalTransactionBalance,
    decimal TotalBudgetedBalance,
    IReadOnlyList<BudgetSnapshotItem> BudgetItems);

public sealed record BudgetSnapshotItem(
    Guid BudgetItemId,
    string Name,
    decimal Balance,
    decimal PlannedCredit,
    decimal PlannedDebit,
    decimal ActualCredit,
    decimal ActualDebit);

namespace BudgetyTzar.Api.Features;

public sealed record TransactionDetail(FinancialTransaction Transaction, IReadOnlyList<TransactionAllocation> Allocations);

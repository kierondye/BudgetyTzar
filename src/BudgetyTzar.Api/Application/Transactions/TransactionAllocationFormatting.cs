namespace BudgetyTzar.Api.Application.Transactions;

internal static class TransactionAllocationFormatting
{
    public static string Format(IEnumerable<TransactionAllocation> allocations) =>
        string.Join("; ", allocations.Select(x => $"{x.BudgetItemId}:{x.Amount}"));

    public static string Format(IEnumerable<TransactionAllocationItem> allocations) =>
        string.Join("; ", allocations.Select(x => $"{x.BudgetItemId}:{x.Amount}"));
}

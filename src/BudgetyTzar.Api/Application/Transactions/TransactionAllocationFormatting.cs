namespace BudgetyTzar.Api.Application.Transactions;

internal static class TransactionAllocationFormatting
{
    public static string Format(IEnumerable<TransactionAllocation> allocations) =>
        string.Join("; ", allocations.Select(x => Format(x.BudgetItemId, x.Amount, x.Notes)));

    public static string Format(IEnumerable<TransactionAllocationItem> allocations) =>
        string.Join("; ", allocations.Select(x => Format(x.BudgetItemId, x.Amount, x.Notes)));

    private static string Format(Guid budgetItemId, decimal amount, string? notes) =>
        string.IsNullOrWhiteSpace(notes)
            ? $"{budgetItemId}:{amount}"
            : $"{budgetItemId}:{amount} ({notes.Trim()})";
}

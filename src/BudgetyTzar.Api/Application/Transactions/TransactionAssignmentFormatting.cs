namespace BudgetyTzar.Api.Application.Transactions;

internal static class TransactionAssignmentFormatting
{
    public static string Format(IEnumerable<TransactionAssignment> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));

    public static string Format(IEnumerable<TransactionAssignmentItem> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));
}

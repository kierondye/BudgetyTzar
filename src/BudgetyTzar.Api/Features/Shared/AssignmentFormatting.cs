namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static string FormatAssignments(IEnumerable<TransactionAssignment> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));

    private static string FormatAssignments(IEnumerable<TransactionAssignmentItem> assignments) =>
        string.Join("; ", assignments.Select(x => $"{x.BudgetLineId}:{x.Amount}"));
}

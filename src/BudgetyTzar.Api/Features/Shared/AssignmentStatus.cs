namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static TransactionAssignmentStatus GetAssignmentStatus(FinancialTransaction transaction, decimal assignedAmount)
    {
        if (assignedAmount == 0)
        {
            return TransactionAssignmentStatus.Unassigned;
        }

        return assignedAmount < transaction.Amount
            ? TransactionAssignmentStatus.PartiallyAssigned
            : TransactionAssignmentStatus.FullyAssigned;
    }
}

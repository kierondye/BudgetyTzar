namespace BudgetyTzar.Api.Features;

public static partial class Endpoints
{
    private static TransactionAllocationStatus GetAllocationStatus(FinancialTransaction transaction, decimal allocatedAmount)
    {
        if (allocatedAmount == 0)
        {
            return TransactionAllocationStatus.Unallocated;
        }

        return allocatedAmount < transaction.Amount
            ? TransactionAllocationStatus.PartiallyAllocated
            : TransactionAllocationStatus.FullyAllocated;
    }
}

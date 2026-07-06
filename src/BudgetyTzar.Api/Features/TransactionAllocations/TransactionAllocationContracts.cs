namespace BudgetyTzar.Api.Features.TransactionAllocations;

public sealed record AllocateTransactionRequest(Guid BudgetItemId);

public sealed record TransactionAllocationResponse(Guid TransactionId, Guid BudgetItemId)
{
    public static TransactionAllocationResponse FromAllocation(TransactionAllocation allocation)
    {
        return new TransactionAllocationResponse(allocation.TransactionId, allocation.BudgetItemId);
    }
}

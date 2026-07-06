namespace BudgetyTzar.Api.Features.TransactionAllocations;

public sealed class TransactionAllocationStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, TransactionAllocation> allocationsByTransactionId = [];

    public AllocateTransactionResult Allocate(Guid transactionId, Guid budgetItemId)
    {
        lock (syncRoot)
        {
            if (allocationsByTransactionId.TryGetValue(transactionId, out var existingAllocation))
            {
                if (existingAllocation.BudgetItemId != budgetItemId)
                {
                    return new AllocateTransactionResult.AlreadyAllocatedToAnotherBudgetItem();
                }

                return new AllocateTransactionResult.Allocated(existingAllocation);
            }

            var allocation = new TransactionAllocation(transactionId, budgetItemId);
            allocationsByTransactionId[transactionId] = allocation;

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.GetValueOrDefault(transactionId);
        }
    }

    public bool Remove(Guid transactionId)
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.Remove(transactionId);
        }
    }

    public bool HasAllocationForTransaction(Guid transactionId)
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.ContainsKey(transactionId);
        }
    }

    public bool HasAllocationForBudgetItem(Guid budgetItemId)
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.Values.Any(allocation => allocation.BudgetItemId == budgetItemId);
        }
    }
}

public sealed record TransactionAllocation(Guid TransactionId, Guid BudgetItemId);

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToAnotherBudgetItem : AllocateTransactionResult;
}

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, TransactionAllocation> allocationsByTransactionId = [];

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        lock (syncRoot)
        {
            if (allocationsByTransactionId.TryGetValue(allocation.TransactionId, out var existingAllocation))
            {
                return existingAllocation.BudgetItemId == allocation.BudgetItemId
                    ? new AllocateTransactionResult.Allocated(existingAllocation)
                    : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
            }

            allocationsByTransactionId[allocation.TransactionId] = allocation;

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

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        lock (syncRoot)
        {
            return allocationsByTransactionId.Values.ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (syncRoot)
        {
            allocationsByTransactionId.Remove(transactionId);
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

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}

using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository
{
    private readonly object syncRoot;
    private readonly Dictionary<Guid, TransactionAllocation> allocationsByTransactionId = [];

    public InMemoryTransactionAllocationRepository(InMemoryDataStoreLock? dataStoreLock = null)
    {
        syncRoot = (dataStoreLock ?? new InMemoryDataStoreLock()).SyncRoot;
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        return Allocate(
            allocation,
            transactionId => true,
            budgetItemId => true);
    }

    public AllocateTransactionResult Allocate(
        TransactionAllocation allocation,
        Func<Guid, bool> transactionExists,
        Func<Guid, bool> budgetItemExists)
    {
        lock (syncRoot)
        {
            if (!transactionExists(allocation.TransactionId))
            {
                return new AllocateTransactionResult.TransactionNotFound();
            }

            if (!budgetItemExists(allocation.BudgetItemId))
            {
                return new AllocateTransactionResult.BudgetItemNotFound();
            }

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

    public sealed record TransactionNotFound : AllocateTransactionResult;

    public sealed record BudgetItemNotFound : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}

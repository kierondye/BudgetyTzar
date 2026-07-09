using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository : ITransactionAllocationRepository
{
    private readonly InMemoryDataStore store;

    public InMemoryTransactionAllocationRepository(InMemoryDataStore? store = null)
    {
        this.store = store ?? new InMemoryDataStore();
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        lock (store.SyncRoot)
        {
            if (!store.TransactionsById.ContainsKey(allocation.TransactionId))
            {
                return new AllocateTransactionResult.TransactionNotFound();
            }

            if (!BudgetItemExists(allocation.BudgetItemId))
            {
                return new AllocateTransactionResult.BudgetItemNotFound();
            }

            if (store.AllocationsByTransactionId.TryGetValue(allocation.TransactionId, out var existingAllocation))
            {
                return existingAllocation.BudgetItemId == allocation.BudgetItemId
                    ? new AllocateTransactionResult.Allocated(existingAllocation)
                    : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
            }

            store.AllocationsByTransactionId[allocation.TransactionId] = allocation;

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId.GetValueOrDefault(transactionId);
        }
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId.Values.ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            store.AllocationsByTransactionId.Remove(transactionId);
        }
    }

    private bool BudgetItemExists(Guid budgetItemId)
    {
        return store.BudgetsById.Values
            .SelectMany(budget => budget.BudgetItems)
            .Any(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }
}

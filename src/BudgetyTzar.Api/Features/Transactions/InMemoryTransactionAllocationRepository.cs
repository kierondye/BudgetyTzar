using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository
{
    private readonly InMemoryDataStore store;

    public InMemoryTransactionAllocationRepository(InMemoryDataStore? store = null)
    {
        this.store = store ?? new InMemoryDataStore();
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        return Allocate(ApplicationUserId.DefaultTestUser, allocation);
    }

    public AllocateTransactionResult Allocate(ApplicationUserId ownerId, TransactionAllocation allocation)
    {
        lock (store.SyncRoot)
        {
            if (!store.TransactionsById.ContainsKey(allocation.TransactionId)
                || store.TransactionOwnersById[allocation.TransactionId] != ownerId)
            {
                return new AllocateTransactionResult.TransactionNotFound();
            }

            if (!BudgetItemExists(ownerId, allocation.BudgetItemId))
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
            store.AllocationOwnersByTransactionId[allocation.TransactionId] = ownerId;

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        return Get(ApplicationUserId.DefaultTestUser, transactionId);
    }

    public TransactionAllocation? Get(ApplicationUserId ownerId, Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId.TryGetValue(transactionId, out var allocation)
                && store.AllocationOwnersByTransactionId[transactionId] == ownerId
                ? allocation
                : null;
        }
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        return GetAll(ApplicationUserId.DefaultTestUser);
    }

    public IReadOnlyList<TransactionAllocation> GetAll(ApplicationUserId ownerId)
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId
                .Where(allocation => store.AllocationOwnersByTransactionId[allocation.Key] == ownerId)
                .Select(allocation => allocation.Value)
                .ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        Remove(ApplicationUserId.DefaultTestUser, transactionId);
    }

    public void Remove(ApplicationUserId ownerId, Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (store.AllocationOwnersByTransactionId.GetValueOrDefault(transactionId) != ownerId)
            {
                return;
            }

            store.AllocationsByTransactionId.Remove(transactionId);
            store.AllocationOwnersByTransactionId.Remove(transactionId);
        }
    }

    private bool BudgetItemExists(ApplicationUserId ownerId, Guid budgetItemId)
    {
        return store.BudgetIds
            .Where(budgetId => store.BudgetOwnersById[budgetId] == ownerId)
            .SelectMany(budgetId => store.BudgetsById[budgetId].BudgetItems)
            .Any(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }
}

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record TransactionNotFound : AllocateTransactionResult;

    public sealed record BudgetItemNotFound : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}

using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository : ITransactionAllocationRepository
{
    private readonly InMemoryDataStore store;
    private readonly ApplicationUserId userId;

    public InMemoryTransactionAllocationRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        userId = currentUser.UserId;
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        lock (store.SyncRoot)
        {
            if (!TransactionBelongsToCurrentUser(allocation.TransactionId)
                || !store.TransactionsById.ContainsKey(allocation.TransactionId))
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
            store.AllocationOwnersByTransactionId[allocation.TransactionId] = userId;

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return AllocationBelongsToCurrentUser(transactionId)
                ? store.AllocationsByTransactionId.GetValueOrDefault(transactionId)
                : null;
        }
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId
                .Where(entry => AllocationBelongsToCurrentUser(entry.Key))
                .Select(entry => entry.Value)
                .ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (AllocationBelongsToCurrentUser(transactionId))
            {
                store.AllocationsByTransactionId.Remove(transactionId);
                store.AllocationOwnersByTransactionId.Remove(transactionId);
            }
        }
    }

    private bool BudgetItemExists(Guid budgetItemId)
    {
        return store.BudgetsById.Values
            .Where(budget => store.BudgetOwnersById.GetValueOrDefault(budget.BudgetId) == userId)
            .SelectMany(budget => budget.BudgetItems)
            .Any(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }

    private bool TransactionBelongsToCurrentUser(Guid transactionId)
    {
        return store.TransactionOwnersById.TryGetValue(transactionId, out var ownerId)
            && ownerId == userId;
    }

    private bool AllocationBelongsToCurrentUser(Guid transactionId)
    {
        return store.AllocationOwnersByTransactionId.TryGetValue(transactionId, out var ownerId)
            && ownerId == userId;
    }
}

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record TransactionNotFound : AllocateTransactionResult;

    public sealed record BudgetItemNotFound : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}

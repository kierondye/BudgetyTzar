using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;
using BudgetyTzar.Api.Features.Identity;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository
{
    private readonly InMemoryDataStore store;
    private readonly ICurrentUser currentUser;

    public InMemoryTransactionAllocationRepository()
        : this(new InMemoryDataStore(), CurrentUser.TestDefault)
    {
    }

    public InMemoryTransactionAllocationRepository(InMemoryDataStore store)
        : this(store, CurrentUser.TestDefault)
    {
    }

    public InMemoryTransactionAllocationRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        this.currentUser = currentUser;
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

            return new AllocateTransactionResult.Allocated(allocation);
        }
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return TransactionBelongsToCurrentUser(transactionId)
                ? store.AllocationsByTransactionId.GetValueOrDefault(transactionId)
                : null;
        }
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        lock (store.SyncRoot)
        {
            return store.AllocationsByTransactionId
                .Where(entry => TransactionBelongsToCurrentUser(entry.Key))
                .Select(entry => entry.Value)
                .ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (TransactionBelongsToCurrentUser(transactionId))
            {
                store.AllocationsByTransactionId.Remove(transactionId);
            }
        }
    }

    private bool BudgetItemExists(Guid budgetItemId)
    {
        return store.BudgetIdsByOwner.GetValueOrDefault(currentUser.UserId, [])
            .Select(budgetId => store.BudgetsById[budgetId])
            .SelectMany(budget => budget.BudgetItems)
            .Any(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }

    private bool TransactionBelongsToCurrentUser(Guid transactionId)
    {
        return store.TransactionOwnersById.TryGetValue(transactionId, out var ownerId)
            && ownerId == currentUser.UserId;
    }
}

public abstract record AllocateTransactionResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionResult;

    public sealed record TransactionNotFound : AllocateTransactionResult;

    public sealed record BudgetItemNotFound : AllocateTransactionResult;

    public sealed record AlreadyAllocatedToDifferentBudgetItem : AllocateTransactionResult;
}

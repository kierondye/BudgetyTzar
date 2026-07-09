using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionAllocationRepository
{
    private readonly InMemoryDataStore store;
    private readonly ICurrentUser currentUser;

    public InMemoryTransactionAllocationRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        this.currentUser = currentUser;
    }

    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            if (!IsTransactionOwner(userId, allocation.TransactionId)
                || !store.TransactionsById.ContainsKey(allocation.TransactionId))
            {
                return new AllocateTransactionResult.TransactionNotFound();
            }

            if (!BudgetItemExists(userId, allocation.BudgetItemId))
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
            return IsTransactionOwner(currentUser.UserId, transactionId)
                ? store.AllocationsByTransactionId.GetValueOrDefault(transactionId)
                : null;
        }
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            return store.AllocationsByTransactionId
                .Where(pair => IsTransactionOwner(userId, pair.Key))
                .Select(pair => pair.Value)
                .ToList();
        }
    }

    public void Remove(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (IsTransactionOwner(currentUser.UserId, transactionId))
            {
                store.AllocationsByTransactionId.Remove(transactionId);
            }
        }
    }

    private bool BudgetItemExists(ApplicationUserId userId, Guid budgetItemId)
    {
        return store.BudgetsById
            .Where(pair => IsBudgetOwner(userId, pair.Key))
            .Select(pair => pair.Value)
            .SelectMany(budget => budget.BudgetItems)
            .Any(budgetItem => budgetItem.BudgetItemId == budgetItemId);
    }

    private bool IsTransactionOwner(ApplicationUserId userId, Guid transactionId)
    {
        return store.TransactionOwnersById.TryGetValue(transactionId, out var ownerId)
            && ownerId == userId;
    }

    private bool IsBudgetOwner(ApplicationUserId userId, Guid budgetId)
    {
        return store.BudgetOwnersById.TryGetValue(budgetId, out var ownerId)
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

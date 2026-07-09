using BudgetyTzar.Api.Authentication;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionRepository
{
    private readonly InMemoryDataStore store;
    private readonly ICurrentUser currentUser;

    public InMemoryTransactionRepository(InMemoryDataStore store, ICurrentUser currentUser)
    {
        this.store = store;
        this.currentUser = currentUser;
    }

    public void Add(Transaction transaction)
    {
        lock (store.SyncRoot)
        {
            store.TransactionsById[transaction.TransactionId] = transaction;
            store.TransactionOwnersById[transaction.TransactionId] = currentUser.UserId;
            store.TransactionIds.Add(transaction.TransactionId);
        }
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        lock (store.SyncRoot)
        {
            var userId = currentUser.UserId;

            return store.TransactionIds
                .Where(transactionId => IsOwner(userId, transactionId))
                .Select(transactionId => store.TransactionsById[transactionId])
                .ToList();
        }
    }

    public Transaction? Get(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return IsOwner(currentUser.UserId, transactionId)
                ? store.TransactionsById.GetValueOrDefault(transactionId)
                : null;
        }
    }

    public TransactionDeleteResult Delete(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (!IsOwner(currentUser.UserId, transactionId)
                || !store.TransactionsById.ContainsKey(transactionId))
            {
                return new TransactionDeleteResult.NotFound();
            }

            if (store.AllocationsByTransactionId.ContainsKey(transactionId))
            {
                return new TransactionDeleteResult.TransactionHasAllocation();
            }

            store.TransactionsById.Remove(transactionId);
            store.TransactionOwnersById.Remove(transactionId);
            store.TransactionIds.Remove(transactionId);
            return new TransactionDeleteResult.Deleted();
        }
    }

    private bool IsOwner(ApplicationUserId userId, Guid transactionId)
    {
        return store.TransactionOwnersById.TryGetValue(transactionId, out var ownerId)
            && ownerId == userId;
    }
}

public abstract record TransactionDeleteResult
{
    public sealed record Deleted : TransactionDeleteResult;

    public sealed record NotFound : TransactionDeleteResult;

    public sealed record TransactionHasAllocation : TransactionDeleteResult;
}

public sealed record TransactionFilters(
    DateOnly? From,
    DateOnly? To,
    TransactionAllocationStatus AllocationStatus)
{
    public bool Matches(Transaction transaction, bool isAllocated)
    {
        if (From is not null && transaction.TransactionDate < From.Value)
        {
            return false;
        }

        if (To is not null && transaction.TransactionDate > To.Value)
        {
            return false;
        }

        return AllocationStatus.Matches(isAllocated);
    }
}

public readonly record struct TransactionAllocationStatus
{
    public static TransactionAllocationStatus All { get; } = new("all");
    public static TransactionAllocationStatus Allocated { get; } = new("allocated");
    public static TransactionAllocationStatus Unallocated { get; } = new("unallocated");

    private TransactionAllocationStatus(string value)
    {
        Value = value;
    }

    private string Value { get; }

    public static bool TryCreate(string? value, out TransactionAllocationStatus allocationStatus)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            allocationStatus = All;
            return true;
        }

        if (string.Equals(value, Allocated.Value, StringComparison.Ordinal))
        {
            allocationStatus = Allocated;
            return true;
        }

        if (string.Equals(value, Unallocated.Value, StringComparison.Ordinal))
        {
            allocationStatus = Unallocated;
            return true;
        }

        if (string.Equals(value, All.Value, StringComparison.Ordinal))
        {
            allocationStatus = All;
            return true;
        }

        allocationStatus = All;
        return false;
    }

    public bool Matches(bool isAllocated)
    {
        if (this == All)
        {
            return true;
        }

        return this == Allocated
            ? isAllocated
            : !isAllocated;
    }
}

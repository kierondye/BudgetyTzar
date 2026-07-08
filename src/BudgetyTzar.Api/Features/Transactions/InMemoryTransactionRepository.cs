using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionRepository
{
    private readonly InMemoryDataStore store;

    public InMemoryTransactionRepository(InMemoryDataStore? store = null)
    {
        this.store = store ?? new InMemoryDataStore();
    }

    public void Add(Transaction transaction)
    {
        lock (store.SyncRoot)
        {
            store.TransactionsById[transaction.TransactionId] = transaction;
            store.TransactionIds.Add(transaction.TransactionId);
        }
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        lock (store.SyncRoot)
        {
            return store.TransactionIds
                .Select(transactionId => store.TransactionsById[transactionId])
                .ToList();
        }
    }

    public Transaction? Get(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            return store.TransactionsById.GetValueOrDefault(transactionId);
        }
    }

    public TransactionDeleteResult Delete(Guid transactionId)
    {
        lock (store.SyncRoot)
        {
            if (!store.TransactionsById.ContainsKey(transactionId))
            {
                return new TransactionDeleteResult.NotFound();
            }

            if (store.AllocationsByTransactionId.ContainsKey(transactionId))
            {
                return new TransactionDeleteResult.TransactionHasAllocation();
            }

            store.TransactionsById.Remove(transactionId);
            store.TransactionIds.Remove(transactionId);
            return new TransactionDeleteResult.Deleted();
        }
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

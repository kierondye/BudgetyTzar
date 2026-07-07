using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class InMemoryTransactionRepository
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Transaction> transactionsById = [];
    private readonly List<Guid> transactionIds = [];

    public void Add(Transaction transaction)
    {
        lock (syncRoot)
        {
            transactionsById[transaction.TransactionId] = transaction;
            transactionIds.Add(transaction.TransactionId);
        }
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        lock (syncRoot)
        {
            return transactionIds
                .Select(transactionId => transactionsById[transactionId])
                .ToList();
        }
    }

    public Transaction? Get(Guid transactionId)
    {
        lock (syncRoot)
        {
            return transactionsById.GetValueOrDefault(transactionId);
        }
    }

    public bool Delete(Guid transactionId)
    {
        lock (syncRoot)
        {
            if (!transactionsById.Remove(transactionId))
            {
                return false;
            }

            transactionIds.Remove(transactionId);
            return true;
        }
    }
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

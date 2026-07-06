using BudgetyTzar.Api.Features.Budgeting;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class TransactionStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Transaction> transactionsById = [];
    private readonly List<Guid> transactionIds = [];

    public Transaction Create(
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        var transaction = new Transaction(
            Guid.NewGuid(),
            description,
            type,
            transactionDate,
            amount,
            currency);

        lock (syncRoot)
        {
            transactionsById[transaction.TransactionId] = transaction;
            transactionIds.Add(transaction.TransactionId);
        }

        return transaction;
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

public sealed record Transaction(
    Guid TransactionId,
    string Description,
    TransactionType Type,
    DateOnly TransactionDate,
    PositiveMoneyAmount Amount,
    CurrencyCode Currency);

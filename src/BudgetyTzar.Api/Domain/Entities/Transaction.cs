using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class Transaction
{
    private Transaction(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        TransactionId = transactionId;
        Description = description;
        Type = type;
        TransactionDate = transactionDate;
        Amount = amount;
        Currency = currency;
    }

    public Guid TransactionId { get; }

    public string Description { get; }

    public TransactionType Type { get; }

    public DateOnly TransactionDate { get; }

    public PositiveMoneyAmount Amount { get; }

    public CurrencyCode Currency { get; }

    public static CreateTransactionResult Create(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        if (transactionId == Guid.Empty)
        {
            return new CreateTransactionResult.InvalidIdentity();
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return new CreateTransactionResult.InvalidDescription();
        }

        return new CreateTransactionResult.Created(
            new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency));
    }
}

public abstract record CreateTransactionResult
{
    public sealed record Created(Transaction Transaction) : CreateTransactionResult;

    public sealed record InvalidIdentity : CreateTransactionResult;

    public sealed record InvalidDescription : CreateTransactionResult;
}

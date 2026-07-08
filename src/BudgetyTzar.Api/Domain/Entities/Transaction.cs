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

    public static RecordTransactionResult Record(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        if (transactionId == Guid.Empty)
        {
            return new RecordTransactionResult.InvalidIdentity();
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return new RecordTransactionResult.InvalidDescription();
        }

        return new RecordTransactionResult.Recorded(
            new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency));
    }
}

public abstract record RecordTransactionResult
{
    public sealed record Recorded(Transaction Transaction) : RecordTransactionResult;

    public sealed record InvalidIdentity : RecordTransactionResult;

    public sealed record InvalidDescription : RecordTransactionResult;
}

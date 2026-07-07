using BudgetyTzar.Api.Features.Common;

namespace BudgetyTzar.Api.Features.Transactions;

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

    public static Transaction Record(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Identity must not be empty.", nameof(transactionId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Transaction description is required.", nameof(description));
        }

        return new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency);
    }
}

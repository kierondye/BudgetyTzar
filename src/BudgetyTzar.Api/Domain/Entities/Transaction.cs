using System.Collections.Immutable;
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
        CurrencyCode currency,
        ImmutableArray<AuditFact> auditFacts)
    {
        TransactionId = transactionId;
        Description = description;
        Type = type;
        TransactionDate = transactionDate;
        Amount = amount;
        Currency = currency;
        AuditFacts = auditFacts;
    }

    public Guid TransactionId { get; }

    public string Description { get; }

    public TransactionType Type { get; }

    public DateOnly TransactionDate { get; }

    public PositiveMoneyAmount Amount { get; }

    public CurrencyCode Currency { get; }

    public ImmutableArray<AuditFact> AuditFacts { get; }

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

        var transaction = new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency, []);
        return new CreateTransactionResult.Created(transaction.WithAuditFact(AuditAction.TransactionCreated, null, transaction));
    }

    internal static Transaction Rehydrate(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        return new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency, []);
    }

    public DeleteTransactionEntityResult Delete()
    {
        return new DeleteTransactionEntityResult.Deleted(WithAuditFact(AuditAction.TransactionDeleted, this, null));
    }

    private Transaction WithAuditFact(AuditAction action, Transaction? oldValue, Transaction? newValue)
    {
        return new Transaction(
            TransactionId,
            Description,
            Type,
            TransactionDate,
            Amount,
            Currency,
            AuditFacts.Add(AuditFact.Create(
                action,
                oldValue is null ? null : AuditValueSerializer.Serialize(oldValue),
                newValue is null ? null : AuditValueSerializer.Serialize(newValue))));
    }
}

public abstract record CreateTransactionResult
{
    public sealed record Created(Transaction Transaction) : CreateTransactionResult;

    public sealed record InvalidIdentity : CreateTransactionResult;

    public sealed record InvalidDescription : CreateTransactionResult;
}

public abstract record DeleteTransactionEntityResult
{
    public sealed record Deleted(Transaction Transaction) : DeleteTransactionEntityResult;
}

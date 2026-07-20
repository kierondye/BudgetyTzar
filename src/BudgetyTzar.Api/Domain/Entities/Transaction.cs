using System.Collections.Immutable;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class Transaction
{
    internal Transaction(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
        : this(transactionId, description.Trim(), type, transactionDate, amount, currency, [])
    {
    }

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

        return new CreateTransactionResult.Created(
            new Transaction(transactionId, description.Trim(), type, transactionDate, amount, currency));
    }

    internal static CreateTransactionResult CreateForCommand(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        return Create(transactionId, description, type, transactionDate, amount, currency) switch
        {
            CreateTransactionResult.Created created => new CreateTransactionResult.Created(
                created.Transaction.WithAuditFact(AuditAction.TransactionCreated, null, created.Transaction)),
            CreateTransactionResult.InvalidIdentity invalid => invalid,
            CreateTransactionResult.InvalidDescription invalid => invalid,
            _ => throw new InvalidOperationException("Unexpected create transaction result.")
        };
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

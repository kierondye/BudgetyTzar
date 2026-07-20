using System.Collections.Immutable;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class TransactionAllocation
{
    private TransactionAllocation(
        Guid transactionId,
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        CurrencyCode currency,
        ImmutableArray<AuditFact> auditFacts)
    {
        TransactionId = transactionId;
        BudgetItemId = budgetItemId;
        Amount = amount;
        Currency = currency;
        AuditFacts = auditFacts;
    }

    public Guid TransactionId { get; }

    public Guid BudgetItemId { get; }

    public PositiveMoneyAmount Amount { get; }

    public CurrencyCode Currency { get; }

    public ImmutableArray<AuditFact> AuditFacts { get; }

    public static AllocateTransactionEntityResult Allocate(Transaction transaction, Guid budgetItemId)
    {
        if (budgetItemId == Guid.Empty)
        {
            return new AllocateTransactionEntityResult.InvalidBudgetItemIdentity();
        }

        var allocation = new TransactionAllocation(
            transaction.TransactionId,
            budgetItemId,
            transaction.Amount,
            transaction.Currency,
            []);

        return new AllocateTransactionEntityResult.Allocated(
            allocation.WithAuditFact(AuditAction.TransactionAllocationCreated, null, allocation));
    }

    internal static TransactionAllocation Rehydrate(
        Guid transactionId,
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        return new TransactionAllocation(transactionId, budgetItemId, amount, currency, []);
    }

    public IdempotentTransactionAllocationEntityResult AllocateIdempotently()
    {
        return new IdempotentTransactionAllocationEntityResult.Allocated(
            WithAuditFact(AuditAction.TransactionAllocationIdempotent, this, this));
    }

    public RemoveTransactionAllocationEntityResult Remove()
    {
        return new RemoveTransactionAllocationEntityResult.Removed(
            WithAuditFact(AuditAction.TransactionAllocationRemoved, this, null));
    }

    private TransactionAllocation WithAuditFact(
        AuditAction action,
        TransactionAllocation? oldValue,
        TransactionAllocation? newValue)
    {
        var target = newValue ?? this;
        var oldSerialized = oldValue is null ? null : AuditValueSerializer.Serialize(oldValue);
        var newSerialized = newValue is null ? null : AuditValueSerializer.Serialize(newValue);

        return new TransactionAllocation(
            target.TransactionId,
            target.BudgetItemId,
            target.Amount,
            target.Currency,
            AuditFacts.Add(AuditFact.Create(action, oldSerialized, newSerialized)));
    }
}

public abstract record AllocateTransactionEntityResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionEntityResult;

    public sealed record InvalidBudgetItemIdentity : AllocateTransactionEntityResult;
}

public abstract record IdempotentTransactionAllocationEntityResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : IdempotentTransactionAllocationEntityResult;
}

public abstract record RemoveTransactionAllocationEntityResult
{
    public sealed record Removed(TransactionAllocation Allocation) : RemoveTransactionAllocationEntityResult;
}

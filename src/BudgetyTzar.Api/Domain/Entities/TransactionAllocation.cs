using System.Collections.Immutable;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class TransactionAllocation
{
    internal TransactionAllocation(
        Guid transactionId,
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
        : this(transactionId, budgetItemId, amount, currency, [])
    {
    }

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

        return new AllocateTransactionEntityResult.Allocated(new TransactionAllocation(
            transaction.TransactionId,
            budgetItemId,
            transaction.Amount,
            transaction.Currency));
    }

    internal static AllocateTransactionEntityResult AllocateForCommand(Transaction transaction, Guid budgetItemId)
    {
        return Allocate(transaction, budgetItemId) switch
        {
            AllocateTransactionEntityResult.Allocated allocated => new AllocateTransactionEntityResult.Allocated(
                allocated.Allocation.WithAuditFact(
                    AuditAction.TransactionAllocationCreated,
                    null,
                    allocated.Allocation)),
            AllocateTransactionEntityResult.InvalidBudgetItemIdentity invalid => invalid,
            _ => throw new InvalidOperationException("Unexpected allocate transaction result.")
        };
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

public abstract record RemoveTransactionAllocationEntityResult
{
    public sealed record Removed(TransactionAllocation Allocation) : RemoveTransactionAllocationEntityResult;
}

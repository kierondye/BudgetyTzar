using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class TransactionAllocation
{
    private TransactionAllocation(
        Guid transactionId,
        Guid budgetItemId,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        TransactionId = transactionId;
        BudgetItemId = budgetItemId;
        Amount = amount;
        Currency = currency;
    }

    public Guid TransactionId { get; }

    public Guid BudgetItemId { get; }

    public PositiveMoneyAmount Amount { get; }

    public CurrencyCode Currency { get; }

    public static AllocateTransactionEntityResult Allocate(Transaction transaction, Guid budgetItemId)
    {
        if (budgetItemId == Guid.Empty)
        {
            return new AllocateTransactionEntityResult.InvalidBudgetItemIdentity();
        }

        return new AllocateTransactionEntityResult.Allocated(
            new TransactionAllocation(
                transaction.TransactionId,
                budgetItemId,
                transaction.Amount,
                transaction.Currency));
    }
}

public abstract record AllocateTransactionEntityResult
{
    public sealed record Allocated(TransactionAllocation Allocation) : AllocateTransactionEntityResult;

    public sealed record InvalidBudgetItemIdentity : AllocateTransactionEntityResult;
}

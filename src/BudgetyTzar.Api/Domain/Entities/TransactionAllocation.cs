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

    public static TransactionAllocation Allocate(Transaction transaction, Guid budgetItemId)
    {
        if (budgetItemId == Guid.Empty)
        {
            throw new ArgumentException("Budget item identity must not be empty.", nameof(budgetItemId));
        }

        return new TransactionAllocation(
            transaction.TransactionId,
            budgetItemId,
            transaction.Amount,
            transaction.Currency);
    }
}

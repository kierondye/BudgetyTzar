using BudgetyTzar.Api.Features.Common;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionDomainTests
{
    [Fact]
    public void Record_normalizes_description()
    {
        var transaction = Transaction.Record(
            Guid.NewGuid(),
            " Salary ",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP"));

        Assert.Equal("Salary", transaction.Description);
    }

    [Fact]
    public void Record_rejects_empty_identity_and_blank_description()
    {
        Assert.Throws<ArgumentException>(() => Transaction.Record(
            Guid.Empty,
            "Salary",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP")));

        Assert.Throws<ArgumentException>(() => Transaction.Record(
            Guid.NewGuid(),
            " ",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP")));
    }

    [Fact]
    public void Allocation_uses_full_transaction_amount_and_currency()
    {
        var transaction = Transaction.Record(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));
        var budgetItemId = Guid.NewGuid();

        var allocation = TransactionAllocation.Allocate(transaction, budgetItemId);

        Assert.Equal(transaction.TransactionId, allocation.TransactionId);
        Assert.Equal(budgetItemId, allocation.BudgetItemId);
        Assert.Same(transaction.Amount, allocation.Amount);
        Assert.Equal(transaction.Currency, allocation.Currency);
    }

    [Fact]
    public void Allocation_rejects_empty_budget_item_identity()
    {
        var transaction = Transaction.Record(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));

        Assert.Throws<ArgumentException>(() => TransactionAllocation.Allocate(transaction, Guid.Empty));
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Invalid test currency.");
    }

    private static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var amount)
            ? amount!
            : throw new InvalidOperationException("Invalid test amount.");
    }
}

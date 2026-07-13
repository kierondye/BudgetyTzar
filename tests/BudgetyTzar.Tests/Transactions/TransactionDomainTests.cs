using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionDomainTests
{
    [Fact]
    public void Create_normalizes_description()
    {
        var created = Assert.IsType<CreateTransactionResult.Created>(Transaction.Create(
            Guid.NewGuid(),
            " Salary ",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP")));

        Assert.Equal("Salary", created.Transaction.Description);
    }

    [Fact]
    public void Create_rejects_empty_identity_and_blank_description()
    {
        var invalidIdentity = Transaction.Create(
            Guid.Empty,
            "Salary",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP"));

        var invalidDescription = Transaction.Create(
            Guid.NewGuid(),
            " ",
            TransactionType.Credit,
            new DateOnly(2026, 7, 1),
            Money("3000.00"),
            Currency("GBP"));

        Assert.IsType<CreateTransactionResult.InvalidIdentity>(invalidIdentity);
        Assert.IsType<CreateTransactionResult.InvalidDescription>(invalidDescription);
    }

    [Fact]
    public void Allocation_uses_full_transaction_amount_and_currency()
    {
        var transaction = CreateTransaction(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));
        var budgetItemId = Guid.NewGuid();

        var allocated = Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, budgetItemId));
        var allocation = allocated.Allocation;

        Assert.Equal(transaction.TransactionId, allocation.TransactionId);
        Assert.Equal(budgetItemId, allocation.BudgetItemId);
        Assert.Same(transaction.Amount, allocation.Amount);
        Assert.Equal(transaction.Currency, allocation.Currency);
    }

    [Fact]
    public void Allocation_rejects_empty_budget_item_identity()
    {
        var transaction = CreateTransaction(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));

        var result = TransactionAllocation.Allocate(transaction, Guid.Empty);

        Assert.IsType<AllocateTransactionEntityResult.InvalidBudgetItemIdentity>(result);
    }

    private static Transaction CreateTransaction(
        Guid transactionId,
        string description,
        TransactionType type,
        DateOnly transactionDate,
        PositiveMoneyAmount amount,
        CurrencyCode currency)
    {
        return Assert.IsType<CreateTransactionResult.Created>(
            Transaction.Create(
                transactionId,
                description,
                type,
                transactionDate,
                amount,
                currency)).Transaction;
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

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
        Assert.Empty(created.Transaction.AuditFacts);
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
    public void Transaction_audit_values_exclude_description_and_audit_facts()
    {
        var transaction = CreateTransaction(
            Guid.NewGuid(),
            "Sensitive supermarket text",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));

        var deleted = Assert.IsType<DeleteTransactionEntityResult.Deleted>(transaction.Delete());
        var fact = Assert.Single(deleted.Transaction.AuditFacts);

        Assert.Equal(AuditAction.TransactionDeleted, fact.Action);
        Assert.NotNull(fact.OldValue);
        Assert.Null(fact.NewValue);
        Assert.DoesNotContain("Sensitive supermarket text", fact.OldValue, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Transaction.AuditFacts), fact.OldValue, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", fact.OldValue, StringComparison.Ordinal);
        Assert.Contains(transaction.TransactionId.ToString(), fact.OldValue, StringComparison.OrdinalIgnoreCase);
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
        Assert.Empty(allocation.AuditFacts);
    }

    [Fact]
    public void Allocation_removal_creates_an_audit_fact_for_the_state_change()
    {
        var transaction = CreateTransaction(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));
        var allocated = Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, Guid.NewGuid()));

        var removed = Assert.IsType<RemoveTransactionAllocationEntityResult.Removed>(
            allocated.Allocation.Remove());

        Assert.Empty(allocated.Allocation.AuditFacts);
        var fact = Assert.Single(removed.Allocation.AuditFacts);
        Assert.Equal(AuditAction.TransactionAllocationRemoved, fact.Action);
        Assert.NotNull(fact.OldValue);
        Assert.Null(fact.NewValue);
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

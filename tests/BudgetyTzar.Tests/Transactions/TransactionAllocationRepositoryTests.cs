using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Transactions;

public sealed class TransactionAllocationRepositoryTests
{
    [Fact]
    public void Allocate_same_transaction_to_same_budget_item_is_idempotent()
    {
        var repository = new InMemoryTransactionAllocationRepository();
        var transaction = CreateTransaction();
        var budgetItemId = Guid.NewGuid();
        var firstAllocation = TransactionAllocation.Allocate(transaction, budgetItemId);
        var secondAllocation = TransactionAllocation.Allocate(transaction, budgetItemId);

        var firstResult = repository.Allocate(firstAllocation);
        var secondResult = repository.Allocate(secondAllocation);

        var firstAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        var secondAllocated = Assert.IsType<AllocateTransactionResult.Allocated>(secondResult);
        Assert.Same(firstAllocated.Allocation, secondAllocated.Allocation);
        Assert.Equal(budgetItemId, repository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    [Fact]
    public void Allocate_same_transaction_to_different_budget_item_conflicts_and_preserves_first_allocation()
    {
        var repository = new InMemoryTransactionAllocationRepository();
        var transaction = CreateTransaction();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();

        var firstResult = repository.Allocate(TransactionAllocation.Allocate(transaction, firstBudgetItemId));
        var secondResult = repository.Allocate(TransactionAllocation.Allocate(transaction, secondBudgetItemId));

        Assert.IsType<AllocateTransactionResult.Allocated>(firstResult);
        Assert.IsType<AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem>(secondResult);
        Assert.Equal(firstBudgetItemId, repository.Get(transaction.TransactionId)?.BudgetItemId);
    }

    private static Transaction CreateTransaction()
    {
        return Transaction.Record(
            Guid.NewGuid(),
            "Groceries",
            TransactionType.Debit,
            new DateOnly(2026, 7, 2),
            Money("42.50"),
            Currency("GBP"));
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

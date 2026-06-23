using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;

namespace BudgetyTzar.Tests;

public sealed class FinancialTransactionTests
{
    [Fact]
    public void TransactionRejectsAllocationsAboveTransactionAmount()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Groceries",
            25m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            transaction.ReplaceAllocations([
                new TransactionAllocationItem(Guid.NewGuid(), 20m),
                new TransactionAllocationItem(Guid.NewGuid(), 5.01m)
            ]));

        Assert.Equal("Total allocated amount cannot exceed the transaction amount.", exception.Message);
    }

    [Fact]
    public void TransactionCanCreateSplitAllocationsWithinAmount()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Split shop",
            30m,
            TransactionDirection.Debit,
            null,
            null,
            null);
        var groceries = Guid.NewGuid();
        var household = Guid.NewGuid();

        var allocations = transaction.ReplaceAllocations([
            new TransactionAllocationItem(groceries, 20m),
            new TransactionAllocationItem(household, 10m)
        ]);

        Assert.Equal(2, allocations.Count);
        Assert.Equal(30m, allocations.Sum(x => x.Amount));
        Assert.Contains(allocations, x => x.BudgetItemId == groceries);
        Assert.Contains(allocations, x => x.BudgetItemId == household);
    }

    [Fact]
    public void TransactionAllocationsTrimOptionalNotes()
    {
        var transaction = FinancialTransaction.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 10),
            "Split shop",
            30m,
            TransactionDirection.Debit,
            null,
            null,
            null);

        var allocation = Assert.Single(transaction.ReplaceAllocations([
            new TransactionAllocationItem(Guid.NewGuid(), 20m, "  Weekly shop  ")
        ]));

        Assert.Equal("Weekly shop", allocation.Notes);
    }
}

using BudgetyTzar.Api;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class DomainBehaviorTests
{
    [Fact]
    public void TransactionRejectsAssignmentsAboveTransactionAmount()
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
            transaction.ReplaceAssignments([
                new TransactionAssignmentItem(Guid.NewGuid(), 20m),
                new TransactionAssignmentItem(Guid.NewGuid(), 5.01m)
            ]));

        Assert.Equal("Total assigned amount cannot exceed the transaction amount.", exception.Message);
    }

    [Fact]
    public void TransactionCanCreateSplitAssignmentsWithinAmount()
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

        var assignments = transaction.ReplaceAssignments([
            new TransactionAssignmentItem(groceries, 20m),
            new TransactionAssignmentItem(household, 10m)
        ]);

        Assert.Equal(2, assignments.Count);
        Assert.Equal(30m, assignments.Sum(x => x.Amount));
        Assert.Contains(assignments, x => x.BudgetLineId == groceries);
        Assert.Contains(assignments, x => x.BudgetLineId == household);
    }

    [Fact]
    public void BudgetLineArchiveChangesStateAndProducesDomainEvent()
    {
        var budgetId = Guid.NewGuid();
        var line = BudgetLine.Create(
            budgetId,
            "Old category",
            BudgetLineDirection.Debit,
            BudgetLineRolloverType.PeriodReset);

        var domainEvent = line.Archive();

        Assert.True(line.IsArchived);
        Assert.Equal("BudgetLineArchived", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(line.Id, domainEvent.EntityId);
    }

    [Fact]
    public void DateRangeDetectsOverlap()
    {
        var june = new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var lateJune = new DateRange(new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 19));
        var july = new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        Assert.True(june.Overlaps(lateJune));
        Assert.False(june.Overlaps(july));
    }
}

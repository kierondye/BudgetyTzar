using BudgetyTzar.Api;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class DomainBehaviorTests
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

    [Fact]
    public void OutboxPublishingLeaseIsClearedOnPublishOrFailure()
    {
        var message = new OutboxMessage
        {
            Topic = "budgetytzar.budgeting.events",
            EventType = "budgetytzar.budgeting.budget-created.v1",
            AggregateId = Guid.NewGuid(),
            AggregateType = nameof(Budget),
            BudgetId = Guid.NewGuid(),
            EnvelopeJson = "{}"
        };
        var lockId = Guid.NewGuid();
        var lockedAt = DateTimeOffset.UtcNow;

        message.Status = OutboxMessageStatus.Publishing;
        message.PublishingLockId = lockId;
        message.PublishingLockedAt = lockedAt;
        Assert.Equal(OutboxMessageStatus.Publishing, message.Status);
        Assert.Equal(lockId, message.PublishingLockId);
        Assert.Equal(lockedAt, message.PublishingLockedAt);

        message.MarkFailed("temporary failure");

        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Null(message.PublishingLockId);
        Assert.Null(message.PublishingLockedAt);

        message.Status = OutboxMessageStatus.Publishing;
        message.PublishingLockId = lockId;
        message.PublishingLockedAt = lockedAt;
        message.MarkPublished(DateTimeOffset.UtcNow);

        Assert.Equal(OutboxMessageStatus.Published, message.Status);
        Assert.Null(message.PublishingLockId);
        Assert.Null(message.PublishingLockedAt);
    }

    [Fact]
    public void BudgetItemArchiveChangesStateAndProducesDomainEvent()
    {
        var budgetId = Guid.NewGuid();
        var item = BudgetItem.Create(budgetId, "Old category");
        var archivedAt = DateTimeOffset.UtcNow;

        var domainEvent = item.Archive(archivedAt);

        Assert.True(item.IsArchived);
        Assert.Equal(archivedAt, item.ArchivedAt);
        Assert.Equal("BudgetItemArchived", domainEvent.EventType);
        Assert.Equal(budgetId, domainEvent.BudgetId);
        Assert.Equal(item.Id, domainEvent.EntityId);
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
